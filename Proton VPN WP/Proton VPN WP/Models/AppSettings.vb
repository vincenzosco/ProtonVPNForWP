' User preferences. Only non-secret values live here; tokens and passwords go
' through CredentialVault.
Namespace Models
    Friend NotInheritable Class AppSettings
        Friend Property PreferredProtocol As ProfileKind = ProfileKind.IkeV2
        Friend Property PreferSecureCore As Boolean = False
        Friend Property OnlyFreeServers As Boolean = False
        Friend Property LastServerName As String
        Friend Property LastServerDomain As String
        Friend Property LastCountryCode As String
        Friend Property AutoRefreshCatalogue As Boolean = True
        Friend Property LastCatalogueRefreshUtc As DateTime = DateTime.MinValue
        Friend Property HasCompletedOnboarding As Boolean = False

        Friend ReadOnly Property CatalogueAge As TimeSpan
            Get
                If LastCatalogueRefreshUtc = DateTime.MinValue Then Return TimeSpan.MaxValue
                Return DateTime.UtcNow - LastCatalogueRefreshUtc
            End Get
        End Property
    End Class
End Namespace
