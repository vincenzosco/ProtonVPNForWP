' One place that owns the long-lived services and the current session, so pages
' never construct their own HTTP client or catalog.
Namespace Services
    Friend NotInheritable Class AppServices

        Private Shared _current As AppServices

        Private ReadOnly _settings As SettingsStore
        Private ReadOnly _vault As CredentialVault
        Private ReadOnly _api As ProtonApiClient
        Private ReadOnly _auth As ProtonAuthService
        Private ReadOnly _catalog As ServerCatalog

        Private _session As Models.ProtonSession
        Private _credentials As Models.VpnCredentials

        Private Sub New()
            _settings = New SettingsStore()
            _vault = New CredentialVault()
            _api = New ProtonApiClient()
            _auth = New ProtonAuthService(_api, New SystemRandomSource(), _vault)
            _catalog = New ServerCatalog(_api, _settings)
        End Sub

        Friend Shared ReadOnly Property Current As AppServices
            Get
                If _current Is Nothing Then _current = New AppServices()
                Return _current
            End Get
        End Property

        Friend ReadOnly Property Settings As SettingsStore
            Get
                Return _settings
            End Get
        End Property

        Friend ReadOnly Property Vault As CredentialVault
            Get
                Return _vault
            End Get
        End Property

        Friend ReadOnly Property Api As ProtonApiClient
            Get
                Return _api
            End Get
        End Property

        Friend ReadOnly Property Auth As ProtonAuthService
            Get
                Return _auth
            End Get
        End Property

        Friend ReadOnly Property Catalog As ServerCatalog
            Get
                Return _catalog
            End Get
        End Property

        ''' <summary>Credentials currently being used for a generated profile.</summary>
        Friend Property Credentials As Models.VpnCredentials
            Get
                Return _credentials
            End Get
            Set(value As Models.VpnCredentials)
                _credentials = value
            End Set
        End Property

        Private _isSignedIn As Boolean
        Private _username As String

        Friend Property Session As Models.ProtonSession
            Get
                Return _session
            End Get
            Set(value As Models.ProtonSession)
                _session = value
                _isSignedIn = value IsNot Nothing AndAlso value.IsAuthenticated
                _username = Nothing
                If value IsNot Nothing Then _username = value.Username
            End Set
        End Property

        Friend ReadOnly Property IsSignedIn As Boolean
            Get
                Return _isSignedIn
            End Get
        End Property

        Friend ReadOnly Property Username As String
            Get
                Return _username
            End Get
        End Property

        Friend Sub SignOut()
            _auth.SignOut()
            _session = Nothing
            _credentials = Nothing
            _isSignedIn = False
            _username = Nothing
        End Sub

    End Class
End Namespace
