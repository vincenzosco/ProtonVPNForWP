' SRP-6a sign-in against Proton's account API.
'
' Flow: POST /auth/info -> SRP proofs locally -> POST /auth -> (optional) /auth/2fa.
' The password never leaves the device; only the SRP proof does.
'
' Two things this class must not lose:
'   * SRP is mutual. The server proves it knows the verifier by returning
'     ServerProof, and the client compares it with the proof it computed.
'     Accepting a 1000 response without that comparison would make the whole
'     exchange pointless, so it is verified on every path that yields a session.
'   * A two-factor attempt is a continuation of one specific /auth exchange. The
'     UID that identifies it is carried across the two calls.
Imports System.Text
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Crypto
Imports Proton_VPN_WP.Models
Imports Windows.Data.Json

Namespace Services
    Friend NotInheritable Class ProtonAuthService

        Private ReadOnly _api As ProtonApiClient
        Private ReadOnly _random As IRandomSource
        Private ReadOnly _vault As CredentialVault

        ''' <summary>UID of a /auth exchange waiting on a second factor.</summary>
        Private _pendingUid As String
        ''' <summary>The server proof that exchange must be validated against.</summary>
        Private _pendingServerProof As Byte()

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
            ' A code completes the attempt already in flight; it must not start a
            ' new SRP exchange, which would invalidate the one the server is holding.
            If Not String.IsNullOrEmpty(twoFactorCode) AndAlso Not String.IsNullOrEmpty(_pendingUid) Then
                Return Await CompleteTwoFactorAsync(username, twoFactorCode)
            End If

            ClearPending()

            If String.IsNullOrEmpty(username) OrElse String.IsNullOrEmpty(password) Then
                Return ApiResult(Of ProtonSession).Failure("Enter your Proton username and password.")
            End If

            ' /auth/info and /auth are unauthenticated calls; a previous session's
            ' bearer token must not ride along on them.
            _api.UserId = Nothing
            _api.AccessToken = Nothing

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

            Dim proofs As SrpProofs
            Try
                ' The password itself is never stored or sent; only the derived proof.
                Dim hashedPassword As Byte() = SrpClient.HashPasswordVersion3(Encoding.UTF8.GetBytes(password), salt, modulus)
                Dim clientSecret As System.Numerics.BigInteger = SrpSecretGenerator.GenerateClientSecret(_random, modulus, bitLength)
                proofs = SrpClient.GenerateProofs(bitLength, modulus, serverEphemeral, hashedPassword, clientSecret)
            Catch ex As Exception
                Return ApiResult(Of ProtonSession).Failure("Could not compute the secure sign-in proof.")
            End Try

            Dim authResult As ApiResult(Of JsonObject) = Await _api.PostAuthAsync(username,
                                                                                  Convert.ToBase64String(proofs.ClientEphemeral),
                                                                                  Convert.ToBase64String(proofs.ClientProof),
                                                                                  srpSession)

            If Not authResult.Ok Then
                ' Code 1001/1002 means "second factor required", not a failure.
                If authResult.Message IsNot Nothing AndAlso authResult.Message.Contains("1001") Then
                    Return BeginTwoFactor(authResult.Value, username, proofs.ExpectedServerProof)
                End If
                Return ApiResult(Of ProtonSession).Failure(authResult.Message)
            End If

            Return Await AcceptAsync(authResult.Value, username, proofs.ExpectedServerProof)
        End Function

        ''' <summary>
        ''' Records the in-flight attempt and asks for the authenticator code. No I/O
        ''' happens here, so this is deliberately not asynchronous: an `Async Function`
        ''' with no `Await` would be reported as BC42356 and would silently run inside a
        ''' continuation less worth reasoning about.
        ''' </summary>
        Private Function BeginTwoFactor(payload As JsonObject, username As String, expectedServerProof As Byte()) As ApiResult(Of ProtonSession)
            _pendingUid = Json.TryString(payload, "UID")
            _pendingServerProof = expectedServerProof

            If String.IsNullOrEmpty(_pendingUid) Then
                Return ApiResult(Of ProtonSession).Failure("Proton asked for a code without identifying the sign-in attempt. Try again.")
            End If

            ' The continuation is authenticated by this UID, not by the bearer token.
            _api.UserId = _pendingUid

            Return ApiResult(Of ProtonSession).Success(New ProtonSession With {
                .Username = username,
                .UserId = _pendingUid,
                .IsTwoFactorPending = True,
                .TwoFactorEnabled = True},
                "Enter the code from your authenticator app.")
        End Function

        Private Async Function CompleteTwoFactorAsync(username As String, code As String) As Task(Of ApiResult(Of ProtonSession))
            Dim result As ApiResult(Of JsonObject) = Await _api.PostTwoFactorAsync(_pendingUid, code)
            If Not result.Ok Then Return ApiResult(Of ProtonSession).Failure(result.Message)

            Dim accepted As ApiResult(Of ProtonSession) = Await AcceptAsync(result.Value, username, _pendingServerProof)
            If accepted.Ok AndAlso Not accepted.Value.IsTwoFactorPending Then ClearPending()
            Return accepted
        End Function

        ''' <summary>
        ''' Validates the server's half of SRP, then turns an accepted response into a
        ''' session. Every path that yields a token goes through here.
        ''' </summary>
        Private Async Function AcceptAsync(payload As JsonObject, username As String, expectedServerProof As Byte()) As Task(Of ApiResult(Of ProtonSession))
            Dim proofFailure As String = VerifyServerProof(payload, expectedServerProof)
            If proofFailure IsNot Nothing Then Return ApiResult(Of ProtonSession).Failure(proofFailure)

            Dim session As ProtonSession = ParseSession(payload, username)
            If session.IsTwoFactorPending Then
                ' A response without a token is still "code required".
                If Not String.IsNullOrEmpty(session.UserId) Then _pendingUid = session.UserId
                _pendingServerProof = expectedServerProof
                Return ApiResult(Of ProtonSession).Success(session, "Enter the code from your authenticator app.")
            End If

            _api.UserId = session.UserId
            _api.AccessToken = session.AccessToken
            Await _vault.StoreSessionAsync(session)
            Return ApiResult(Of ProtonSession).Success(session)
        End Function

        ''' <summary>
        ''' Compares the server's proof with the one this device computed.
        ''' </summary>
        ''' <returns>Nothing when the proof is valid, otherwise the message to show.</returns>
        Private Shared Function VerifyServerProof(payload As JsonObject, expected As Byte()) As String
            Const Rejected As String = "Proton did not prove it knows your password, so the sign-in was stopped."

            If expected Is Nothing Then Return "The secure sign-in proof was lost. Start again."

            Dim receivedText As String = Json.TryString(payload, "ServerProof")
            ' Fail closed: an absent proof is indistinguishable from a forged one.
            If String.IsNullOrEmpty(receivedText) Then Return Rejected

            Dim received As Byte()
            Try
                received = Convert.FromBase64String(receivedText)
            Catch ex As Exception
                Return "Proton returned an unreadable sign-in proof, so the sign-in was stopped."
            End Try

            If Not CryptoBytes.ConstantTimeEquals(expected, received) Then Return Rejected
            Return Nothing
        End Function

        ''' <summary>
        ''' Restores a stored session, refreshing it first when its lifetime has run
        ''' out. Without this an expired token would be used until every call failed
        ''' and the app silently dropped to the bundled catalogue.
        ''' </summary>
        Friend Async Function RestoreAsync() As Task(Of ProtonSession)
            Dim stored As ProtonSession = Await _vault.LoadSessionAsync()
            If stored Is Nothing OrElse Not stored.IsAuthenticated Then Return Nothing

            If stored.HasExpired AndAlso Not String.IsNullOrEmpty(stored.RefreshToken) Then
                Dim refreshed As ApiResult(Of JsonObject) = Await _api.RefreshAsync(stored.UserId, stored.RefreshToken)
                If refreshed.Ok Then
                    Dim renewed As ProtonSession = ParseSession(refreshed.Value, stored.Username)
                    If renewed.IsAuthenticated Then
                        If String.IsNullOrEmpty(renewed.UserId) Then renewed.UserId = stored.UserId
                        If String.IsNullOrEmpty(renewed.RefreshToken) Then renewed.RefreshToken = stored.RefreshToken
                        Await _vault.StoreSessionAsync(renewed)
                        _api.UserId = renewed.UserId
                        _api.AccessToken = renewed.AccessToken
                        Log.Info("Refreshed an expired Proton session.")
                        Return renewed
                    End If
                End If
                Log.Warn("Could not refresh the stored session; using the existing token.")
            End If

            _api.UserId = stored.UserId
            _api.AccessToken = stored.AccessToken
            Return stored
        End Function

        Friend Sub SignOut()
            _api.UserId = Nothing
            _api.AccessToken = Nothing
            ClearPending()
            _vault.Clear()
        End Sub

        Private Sub ClearPending()
            _pendingUid = Nothing
            _pendingServerProof = Nothing
        End Sub

        Private Shared Function ParseSession(payload As JsonObject, username As String) As ProtonSession
            Dim session As New ProtonSession()
            session.Username = username
            session.UserId = Json.TryString(payload, "UID")
            session.AccessToken = Json.TryString(payload, "AccessToken")
            session.RefreshToken = Json.TryString(payload, "RefreshToken")
            session.Scope = Json.TryString(payload, "Scope")
            session.ExpiresIn = Json.TryInt(payload, "ExpiresIn", 0)
            session.IssuedUtc = DateTime.UtcNow

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
        ''' on TLS to the account host, plus the server proof check above. Recorded as
        ''' a ruling.
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
