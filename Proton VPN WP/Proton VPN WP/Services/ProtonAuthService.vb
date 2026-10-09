' SRP-6a sign-in against Proton's account API.
'
' Flow: POST /auth/info -> SRP proofs locally -> POST /auth -> (optional) /auth/2fa.
' The password never leaves the device; only the SRP proof does.
Imports System.Text
Imports System.Threading.Tasks
Imports Crypto
Imports Models
Imports Windows.Data.Json

Namespace Services
    Friend NotInheritable Class ProtonAuthService

        Private ReadOnly _api As ProtonApiClient
        Private ReadOnly _random As IRandomSource
        Private ReadOnly _vault As CredentialVault

        Friend Sub New(api As ProtonApiClient, random As IRandomSource, vault As CredentialVault)
            _api = api
            _random = random
            _vault = vault
        End Sub

        ''' <summary>
        ''' Signs in. When Proton asks for a second factor, returns a session with
        ''' IsTwoFactorPending set and no token; call again passing the code.
        ''' </summary>
        Friend Async Function SignInAsync(username As String, password As String, twoFactorCode As String) As Task(Of ApiResult(Of ProtonSession))
            If String.IsNullOrEmpty(username) OrElse String.IsNullOrEmpty(password) Then
                Return ApiResult(Of ProtonSession).Failure("Enter your Proton username and password.")
            End If

            Dim infoResult As ApiResult(Of JsonObject) = Await _api.GetAuthInfoAsync(username)
            If Not infoResult.Ok Then Return ApiResult(Of ProtonSession).Failure(infoResult.Message)

            Dim modulusText As String = ExtractClearsignedPayload(Json.TryString(infoResult.Value, "Modulus"))
            Dim ephemeralText As String = Json.TryString(infoResult.Value, "ServerEphemeral")
            Dim saltText As String = Json.TryString(infoResult.Value, "Salt")
            Dim srpSession As String = Json.TryString(infoResult.Value, "SRPSession")

            If String.IsNullOrEmpty(modulusText) OrElse String.IsNullOrEmpty(ephemeralText) OrElse String.IsNullOrEmpty(srpSession) Then
                Return ApiResult(Of ProtonSession).Failure("Proton did not return the data needed for a secure sign-in.")
            End If

            Dim modulus As Byte()
            Dim serverEphemeral As Byte()
            Dim salt As Byte()
            Try
                modulus = Convert.FromBase64String(modulusText)
                serverEphemeral = Convert.FromBase64String(ephemeralText)
                salt = Convert.FromBase64String(saltText)
            Catch ex As Exception
                Return ApiResult(Of ProtonSession).Failure("Proton returned a malformed SRP challenge.")
            End Try

            Dim bitLength As Integer = modulus.Length * 8

            Dim hashedPassword As Byte()
            Dim proofs As SrpProofs
            Try
                ' The password itself is never stored or sent; only the derived proof.
                hashedPassword = SrpClient.HashPasswordVersion3(Encoding.UTF8.GetBytes(password), salt, modulus)
                Dim clientSecret = SrpSecretGenerator.GenerateClientSecret(_random, modulus, bitLength)
                proofs = SrpClient.GenerateProofs(bitLength, modulus, serverEphemeral, hashedPassword, clientSecret)
            Catch ex As Exception
                Return ApiResult(Of ProtonSession).Failure("Could not compute the secure sign-in proof.")
            End Try

            Dim authResult As ApiResult(Of JsonObject)
            If Not String.IsNullOrEmpty(twoFactorCode) Then
                authResult = Await _api.PostTwoFactorAsync(username, twoFactorCode)
            Else
                authResult = Await _api.PostAuthAsync(username,
                                                      Convert.ToBase64String(proofs.ClientEphemeral),
                                                      Convert.ToBase64String(proofs.ClientProof),
                                                      srpSession)
            End If

            If Not authResult.Ok Then
                ' Code 1001/1002 means "second factor required", not a failure.
                If authResult.Message IsNot Nothing AndAlso authResult.Message.Contains("1001") Then
                    Return ApiResult(Of ProtonSession).Success(New ProtonSession With {
                        .Username = username,
                        .IsTwoFactorPending = True,
                        .TwoFactorEnabled = True},
                        "Enter the code from your authenticator app.")
                End If
                Return ApiResult(Of ProtonSession).Failure(authResult.Message)
            End If

            Dim session As ProtonSession = ParseSession(authResult.Value, username)
            If session.IsTwoFactorPending Then
                Return ApiResult(Of ProtonSession).Success(session, "Enter the code from your authenticator app.")
            End If

            _api.UserId = session.UserId
            _api.AccessToken = session.AccessToken
            Await _vault.StoreSessionAsync(session)
            Return ApiResult(Of ProtonSession).Success(session)
        End Function

        Friend Async Function RestoreAsync() As Task(Of ProtonSession)
            Dim stored As ProtonSession = Await _vault.LoadSessionAsync()
            If stored Is Nothing OrElse Not stored.IsAuthenticated Then Return Nothing
            _api.UserId = stored.UserId
            _api.AccessToken = stored.AccessToken
            Return stored
        End Function

        Friend Sub SignOut()
            _api.UserId = Nothing
            _api.AccessToken = Nothing
            _vault.Clear()
        End Sub

        Private Shared Function ParseSession(payload As JsonObject, username As String) As ProtonSession
            Dim session As New ProtonSession()
            session.Username = username
            session.UserId = Json.TryString(payload, "UID")
            session.AccessToken = Json.TryString(payload, "AccessToken")
            session.RefreshToken = Json.TryString(payload, "RefreshToken")
            session.Scope = Json.TryString(payload, "Scope")
            session.ExpiresIn = Json.TryInt(payload, "ExpiresIn", 0)

            Dim twoFactor As JsonObject = Json.TryObject(payload, "2FA")
            If twoFactor Is Nothing Then twoFactor = Json.TryObject(payload, "TwoFactor")
            If twoFactor IsNot Nothing Then
                session.TwoFactorEnabled = Json.TryInt(twoFactor, "Enabled", 0) <> 0
            End If

            ' A response without a token still means "second factor required".
            session.IsTwoFactorPending = String.IsNullOrEmpty(session.AccessToken)
            Return session
        End Function

        ''' <summary>
        ''' Strips the PGP cleartext-signature wrapper Proton puts around the SRP
        ''' modulus, including undoing dash-escaping, and returns the base64 payload.
        '''
        ''' NOTE: the signature itself is NOT cryptographically verified here. Go's
        ''' go-srp verifies it against Proton's modulus public key; doing the same in
        ''' VB would mean shipping an OpenPGP verifier. Authenticity therefore rests
        ''' on TLS to the account host. Recorded as a ruling in the ledger.
        ''' </summary>
        Friend Shared Function ExtractClearsignedPayload(signedText As String) As String
            If String.IsNullOrEmpty(signedText) Then Return Nothing
            If Not signedText.Contains("-----BEGIN PGP SIGNED MESSAGE-----") Then Return signedText.Trim()

            Dim lines As String() = signedText.Replace(vbCrLf, vbLf).Split(CChar(vbLf))
            Dim payload As New StringBuilder()
            Dim inBody As Boolean = False

            For Each rawLine As String In lines
                Dim line As String = rawLine
                If Not inBody Then
                    If line.StartsWith("-----BEGIN PGP SIGNED MESSAGE-----") Then Continue For
                    If line.StartsWith("Hash:") Then Continue For
                    If line.Length = 0 Then
                        inBody = True
                        Continue For
                    End If
                    Continue For
                End If

                If line.StartsWith("-----BEGIN PGP SIGNATURE-----") Then Exit For
                ' Cleartext signing escapes lines that start with a dash.
                If line.StartsWith("- ") Then line = line.Substring(2)
                payload.Append(line)
                payload.Append(vbLf)
            Next

            Return payload.ToString().Trim()
        End Function

    End Class
End Namespace
