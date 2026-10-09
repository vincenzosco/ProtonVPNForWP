' Secret storage: access/refresh tokens and the OpenVPN/IKEv2 password.
'
' Values are encrypted with Windows' DataProtectionProvider ("LOCAL=user") before
' they touch ApplicationData, so a copied settings file is not a credential dump.
Imports System.Threading.Tasks
Imports Windows.Security.Cryptography
Imports Windows.Security.Cryptography.DataProtection
Imports Windows.Storage
Imports Windows.Storage.Streams

Namespace Services
    Friend NotInheritable Class CredentialVault

        Private Const Descriptor As String = "LOCAL=user"
        Private Const KeyAccessToken As String = "sec.accessToken"
        Private Const KeyRefreshToken As String = "sec.refreshToken"
        Private Const KeyUserId As String = "sec.userId"
        Private Const KeyUsername As String = "sec.username"
        Private Const KeyVpnUsername As String = "sec.vpnUsername"
        Private Const KeyVpnPassword As String = "sec.vpnPassword"
        Private Const KeyExpiresIn As String = "sec.expiresIn"
        Private Const KeyIssuedUtc As String = "sec.issuedUtc"

        Private ReadOnly _values As ApplicationDataContainer

        Friend Sub New()
            _values = ApplicationData.Current.LocalSettings
        End Sub

        Friend Async Function StoreSessionAsync(session As Models.ProtonSession) As Task
            If session Is Nothing Then Return
            Await StoreAsync(KeyAccessToken, session.AccessToken)
            Await StoreAsync(KeyRefreshToken, session.RefreshToken)
            Await StoreAsync(KeyUserId, session.UserId)
            Await StoreAsync(KeyUsername, session.Username)
            ' The lifetime is persisted too, otherwise a restored session can never
            ' report itself expired and no refresh is ever attempted.
            Await StoreAsync(KeyExpiresIn, session.ExpiresIn.ToString(System.Globalization.CultureInfo.InvariantCulture))
            Await StoreAsync(KeyIssuedUtc, session.IssuedUtc.ToString("o", System.Globalization.CultureInfo.InvariantCulture))
        End Function

        Friend Async Function LoadSessionAsync() As Task(Of Models.ProtonSession)
            Dim session As New Models.ProtonSession()
            session.AccessToken = Await LoadAsync(KeyAccessToken)
            session.RefreshToken = Await LoadAsync(KeyRefreshToken)
            session.UserId = Await LoadAsync(KeyUserId)
            session.Username = Await LoadAsync(KeyUsername)

            Dim expiresText As String = Await LoadAsync(KeyExpiresIn)
            Dim expires As Integer = 0
            If Not String.IsNullOrEmpty(expiresText) Then Integer.TryParse(expiresText, expires)
            session.ExpiresIn = expires

            Dim issuedText As String = Await LoadAsync(KeyIssuedUtc)
            If Not String.IsNullOrEmpty(issuedText) Then
                ' Round-trip on the invariant culture. The value is written as
                ' "2026-10-09T15:28:28.7215520Z"; the culture-sensitive overload would
                ' read that back as local time (Kind = Local), shifting the instant by
                ' the machine's UTC offset. HasExpired compares IssuedUtc against
                ' DateTime.UtcNow, so the shift would postpone expiry by that many
                ' hours and the app would keep using a dead token instead of
                ' refreshing it. RoundtripKind keeps Kind = Utc and the true instant.
                Dim issued As DateTime
                If DateTime.TryParse(issuedText, System.Globalization.CultureInfo.InvariantCulture,
                                     System.Globalization.DateTimeStyles.RoundtripKind, issued) Then
                    session.IssuedUtc = issued.ToUniversalTime()
                End If
            End If

            Return session
        End Function

        Friend Async Function StoreCredentialsAsync(credentials As Models.VpnCredentials) As Task
            If credentials Is Nothing Then Return
            Await StoreAsync(KeyVpnUsername, credentials.Username)
            Await StoreAsync(KeyVpnPassword, credentials.Password)
        End Function

        Friend Async Function LoadCredentialsAsync() As Task(Of Models.VpnCredentials)
            Dim credentials As New Models.VpnCredentials()
            credentials.Username = Await LoadAsync(KeyVpnUsername)
            credentials.Password = Await LoadAsync(KeyVpnPassword)
            Return credentials
        End Function

        Friend Sub Clear()
            For Each key As String In {KeyAccessToken, KeyRefreshToken, KeyUserId, KeyUsername, KeyVpnUsername, KeyVpnPassword, KeyExpiresIn, KeyIssuedUtc}
                _values.Values.Remove(key)
            Next
        End Sub

        Private Async Function StoreAsync(key As String, plain As String) As Task
            If String.IsNullOrEmpty(plain) Then
                _values.Values.Remove(key)
                Return
            End If
            Try
                ' "protected" is a VB keyword, so the value needs a real name.
                Dim protectedValue As String = Await ProtectAsync(plain)
                _values.Values(key) = protectedValue
            Catch ex As Exception
                Log.Warn("Could not protect a credential for storage: " & ex.Message)
            End Try
        End Function

        Private Async Function LoadAsync(key As String) As Task(Of String)
            Dim raw As Object = Nothing
            If Not _values.Values.TryGetValue(key, raw) OrElse raw Is Nothing Then Return Nothing
            Try
                Return Await UnprotectAsync(raw.ToString())
            Catch ex As Exception
                Log.Warn("Could not unprotect a stored credential: " & ex.Message)
                Return Nothing
            End Try
        End Function

        Private Shared Async Function ProtectAsync(plain As String) As Task(Of String)
            Dim provider As New DataProtectionProvider(Descriptor)
            Dim buffer As IBuffer = CryptographicBuffer.ConvertStringToBinary(plain, BinaryStringEncoding.Utf8)
            Dim encrypted As IBuffer = Await provider.ProtectAsync(buffer)
            Return CryptographicBuffer.EncodeToBase64String(encrypted)
        End Function

        Private Shared Async Function UnprotectAsync(encoded As String) As Task(Of String)
            Dim provider As New DataProtectionProvider()
            Dim buffer As IBuffer = CryptographicBuffer.DecodeFromBase64String(encoded)
            Dim decrypted As IBuffer = Await provider.UnprotectAsync(buffer)
            Return CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, decrypted)
        End Function

    End Class
End Namespace
