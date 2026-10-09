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
        End Function

        Friend Async Function LoadSessionAsync() As Task(Of Models.ProtonSession)
            Dim session As New Models.ProtonSession()
            session.AccessToken = Await LoadAsync(KeyAccessToken)
            session.RefreshToken = Await LoadAsync(KeyRefreshToken)
            session.UserId = Await LoadAsync(KeyUserId)
            session.Username = Await LoadAsync(KeyUsername)
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
            For Each key As String In {KeyAccessToken, KeyRefreshToken, KeyUserId, KeyUsername, KeyVpnUsername, KeyVpnPassword}
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
