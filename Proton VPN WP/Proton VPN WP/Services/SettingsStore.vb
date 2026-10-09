' Persists non-secret preferences in ApplicationData.LocalSettings.
' Secrets go to CredentialVault instead.
Imports Models
Imports Windows.Storage

Namespace Services
    Friend NotInheritable Class SettingsStore

        Private Const KeyProtocol As String = "pref.protocol"
        Private Const KeySecureCore As String = "pref.secureCore"
        Private Const KeyOnlyFree As String = "pref.onlyFree"
        Private Const KeyLastServerName As String = "pref.lastServerName"
        Private Const KeyLastServerDomain As String = "pref.lastServerDomain"
        Private Const KeyLastCountry As String = "pref.lastCountry"
        Private Const KeyAutoRefresh As String = "pref.autoRefresh"
        Private Const KeyLastRefresh As String = "pref.lastRefreshUtc"
        Private Const KeyOnboarding As String = "pref.onboarding"

        Private ReadOnly _values As ApplicationDataContainer

        Friend Sub New()
            _values = ApplicationData.Current.LocalSettings
        End Sub

        Friend Function Load() As AppSettings
            Dim settings As New AppSettings()
            settings.PreferredProtocol = CType(ReadInt(KeyProtocol, CInt(ProfileKind.IkeV2)), ProfileKind)
            settings.PreferSecureCore = ReadBool(KeySecureCore, False)
            settings.OnlyFreeServers = ReadBool(KeyOnlyFree, False)
            settings.LastServerName = ReadString(KeyLastServerName)
            settings.LastServerDomain = ReadString(KeyLastServerDomain)
            settings.LastCountryCode = ReadString(KeyLastCountry)
            settings.AutoRefreshCatalogue = ReadBool(KeyAutoRefresh, True)
            settings.LastCatalogueRefreshUtc = ReadDate(KeyLastRefresh)
            settings.HasCompletedOnboarding = ReadBool(KeyOnboarding, False)
            Return settings
        End Function

        Friend Sub Save(settings As AppSettings)
            If settings Is Nothing Then Return
            Write(KeyProtocol, CInt(settings.PreferredProtocol))
            Write(KeySecureCore, settings.PreferSecureCore)
            Write(KeyOnlyFree, settings.OnlyFreeServers)
            Write(KeyLastServerName, settings.LastServerName)
            Write(KeyLastServerDomain, settings.LastServerDomain)
            Write(KeyLastCountry, settings.LastCountryCode)
            Write(KeyAutoRefresh, settings.AutoRefreshCatalogue)
            Write(KeyLastRefresh, If(settings.LastCatalogueRefreshUtc = DateTime.MinValue,
                                      CType(Nothing, String),
                                      settings.LastCatalogueRefreshUtc.ToString("o")))
            Write(KeyOnboarding, settings.HasCompletedOnboarding)
        End Sub

        Friend Sub Reset()
            _values.Values.Clear()
        End Sub

        Private Sub Write(key As String, value As Object)
            If value Is Nothing Then
                _values.Values.Remove(key)
            Else
                _values.Values(key) = value
            End If
        End Sub

        Private Function ReadString(key As String) As String
            Dim raw As Object = Nothing
            If _values.Values.TryGetValue(key, raw) AndAlso raw IsNot Nothing Then
                Return raw.ToString()
            End If
            Return Nothing
        End Function

        Private Function ReadBool(key As String, fallback As Boolean) As Boolean
            Dim raw As Object = Nothing
            If _values.Values.TryGetValue(key, raw) AndAlso TypeOf raw Is Boolean Then
                Return CBool(raw)
            End If
            Return fallback
        End Function

        Private Function ReadInt(key As String, fallback As Integer) As Integer
            Dim raw As Object = Nothing
            If _values.Values.TryGetValue(key, raw) AndAlso TypeOf raw Is Integer Then
                Return CInt(raw)
            End If
            Return fallback
        End Function

        Private Function ReadDate(key As String) As DateTime
            Dim text As String = ReadString(key)
            Dim parsed As DateTime
            If Not String.IsNullOrEmpty(text) AndAlso DateTime.TryParse(text, parsed) Then
                Return parsed
            End If
            Return DateTime.MinValue
        End Function

    End Class
End Namespace
