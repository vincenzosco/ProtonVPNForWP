' Builds the VPN profile for the chosen server and exposes the exact fields the
' Windows Phone "Add VPN" dialog asks for.
Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Models
Imports Proton_VPN_WP.Services

Namespace ViewModels
    Friend NotInheritable Class ProfileViewModel
        Inherits ViewModelBase

        Private Const MaskedPassword As String = "••••••••••"

        Private _serverName As String = "No server selected"
        Private _serverAddress As String = "—"
        Private _locationLine As String = "Pick a server from the Servers page first."
        Private _kindText As String = "IKEv2 / IPsec"
        Private _username As String = "—"
        Private _password As String
        Private _revealPassword As Boolean
        Private _hasProfile As Boolean
        Private _notice As String
        Private _profile As VpnProfile
        Private _credentialSourceLine As String = "Not set"

        ' VB12 has no read-only auto-properties, so the collection is a backing field
        ' exposed through an explicit getter.
        Private ReadOnly _fields As New ObservableCollection(Of ProfileFieldRow)()

        Friend ReadOnly Property Fields As ObservableCollection(Of ProfileFieldRow)
            Get
                Return _fields
            End Get
        End Property

        Friend Property ServerName As String
            Get
                Return If(_serverName, String.Empty)
            End Get
            Set(value As String)
                SetProperty(_serverName, value, "ServerName")
            End Set
        End Property

        Friend Property ServerAddress As String
            Get
                Return If(_serverAddress, String.Empty)
            End Get
            Set(value As String)
                SetProperty(_serverAddress, value, "ServerAddress")
            End Set
        End Property

        Friend Property LocationLine As String
            Get
                Return If(_locationLine, String.Empty)
            End Get
            Set(value As String)
                SetProperty(_locationLine, value, "LocationLine")
            End Set
        End Property

        Friend Property KindText As String
            Get
                Return If(_kindText, String.Empty)
            End Get
            Set(value As String)
                SetProperty(_kindText, value, "KindText")
            End Set
        End Property

        Friend Property Username As String
            Get
                Return If(_username, String.Empty)
            End Get
            Set(value As String)
                SetProperty(_username, value, "Username")
            End Set
        End Property

        Friend Property CredentialSourceLine As String
            Get
                Return If(_credentialSourceLine, String.Empty)
            End Get
            Set(value As String)
                SetProperty(_credentialSourceLine, value, "CredentialSourceLine")
            End Set
        End Property

        Friend ReadOnly Property PasswordDisplay As String
            Get
                If String.IsNullOrEmpty(_password) Then Return MaskedPassword
                Return If(_revealPassword, _password, MaskedPassword)
            End Get
        End Property

        Friend Property RevealPassword As Boolean
            Get
                Return _revealPassword
            End Get
            Set(value As Boolean)
                If SetProperty(_revealPassword, value, "RevealPassword") Then
                    RaisePropertyChanged("PasswordDisplay")
                End If
            End Set
        End Property

        Friend Property HasProfile As Boolean
            Get
                Return _hasProfile
            End Get
            Set(value As Boolean)
                If SetProperty(_hasProfile, value, "HasProfile") Then
                    RaisePropertyChanged("HasNoProfile")
                End If
            End Set
        End Property

        Friend ReadOnly Property HasNoProfile As Boolean
            Get
                Return Not _hasProfile
            End Get
        End Property

        Friend Property NoticeLine As String
            Get
                Return If(_notice, String.Empty)
            End Get
            Set(value As String)
                SetProperty(_notice, value, "NoticeLine")
            End Set
        End Property

        Friend ReadOnly Property Profile As VpnProfile
            Get
                Return _profile
            End Get
        End Property

        ''' <summary>
        ''' True when the selected profile is an OpenVPN export. An OpenVPN profile
        ''' cannot be entered into the built-in VPN client, so the page has to say so
        ''' and show the configuration instead of a type it would reject.
        ''' </summary>
        Friend ReadOnly Property ShowOpenVpnConfig As Boolean
            Get
                Return _profile IsNot Nothing AndAlso _profile.Kind = ProfileKind.OpenVpn
            End Get
        End Property

        Friend ReadOnly Property OpenVpnConfigText As String
            Get
                If _profile Is Nothing Then Return String.Empty
                Return If(_profile.OpenVpnText, String.Empty)
            End Get
        End Property

        Friend Async Function LoadAsync() As Task
            IsBusy = True
            StatusMessage = "Preparing your profile…"
            Try
                Dim settings As AppSettings = AppServices.Current.Settings.Load()
                Dim server As ProtonLogical = ResolveServer(settings)

                Dim credentials As VpnCredentials = AppServices.Current.Credentials
                If credentials Is Nothing OrElse Not credentials.IsComplete Then
                    credentials = Await AppServices.Current.Vault.LoadCredentialsAsync()
                    AppServices.Current.Credentials = credentials
                End If

                Dim kind As ProfileKind = settings.PreferredProtocol
                _profile = VpnProfileBuilder.Build(kind, server, credentials)

                If server Is Nothing Then
                    HasProfile = False
                    ServerName = "No server selected"
                    ServerAddress = "—"
                    LocationLine = "Pick a server from the Servers page first."
                Else
                    HasProfile = True
                    ServerName = _profile.ServerName
                    ServerAddress = _profile.ServerAddress
                    LocationLine = server.LocationText & " · " & server.FeatureText
                End If

                KindText = _profile.KindText
                Username = If(String.IsNullOrEmpty(_profile.Username), "—", _profile.Username)
                _password = _profile.Password
                RaisePropertyChanged("PasswordDisplay")
                RaisePropertyChanged("ShowOpenVpnConfig")
                RaisePropertyChanged("OpenVpnConfigText")
                CredentialSourceLine = DescribeCredentials(credentials)
                RebuildFields()
                UpdateNotice(server, credentials)
                StatusMessage = Nothing
            Finally
                IsBusy = False
            End Try
        End Function

        ''' <summary>
        ''' The remembered server if it still exists, otherwise the least loaded one --
        ''' so this page is never usefully blank.
        ''' </summary>
        Private Shared Function ResolveServer(settings As AppSettings) As ProtonLogical
            Dim catalog As ServerCatalog = AppServices.Current.Catalog
            Dim remembered As ProtonLogical = catalog.FindByName(settings.LastServerName)
            If remembered IsNot Nothing Then Return remembered
            Return catalog.FindFastestInCountry(settings.LastCountryCode, settings.OnlyFreeServers)
        End Function

        Private Shared Function DescribeCredentials(credentials As VpnCredentials) As String
            If credentials Is Nothing OrElse Not credentials.IsComplete Then
                Return "No OpenVPN/IKEv2 credentials yet — enter them on the Account page or in Settings."
            End If
            If credentials.Source = CredentialSource.Api Then
                Return "Credentials fetched from your Proton account."
            End If
            Return "Credentials entered manually."
        End Function

        Private Sub UpdateNotice(server As ProtonLogical, credentials As VpnCredentials)
            If server Is Nothing Then
                NoticeLine = "No server is selected, so the profile below only shows default values."
            ElseIf credentials Is Nothing OrElse Not credentials.IsComplete Then
                NoticeLine = "Windows Phone will reject the sign-in until the OpenVPN/IKEv2 username and password are filled in."
            Else
                NoticeLine = Nothing
            End If
        End Sub

        ''' <summary>
        ''' The rows mirror the OS dialog's own field order so the user can read one
        ''' line and type one line.
        ''' </summary>
        Private Sub RebuildFields()
            Fields.Clear()
            If _profile Is Nothing Then Return

            Dim passwordValue As String = If(String.IsNullOrEmpty(_password), "—", PasswordDisplay)
            Dim rows As New List(Of ProfileFieldRow)()
            rows.Add(New ProfileFieldRow With {.Ordinal = 1, .Label = "Server name", .Value = _profile.ServerName})
            rows.Add(New ProfileFieldRow With {.Ordinal = 2, .Label = "Server address", .Value = _profile.ServerAddress})
            ' Must follow the selected protocol, not assume IKEv2.
            rows.Add(New ProfileFieldRow With {.Ordinal = 3, .Label = "VPN type", .Value = _profile.VpnTypeValue})
            rows.Add(New ProfileFieldRow With {.Ordinal = 4, .Label = "Type of sign-in info", .Value = "User name and password"})
            rows.Add(New ProfileFieldRow With {.Ordinal = 5, .Label = "User name", .Value = Username})
            rows.Add(New ProfileFieldRow With {.Ordinal = 6, .Label = "Password", .Value = passwordValue, .IsSecret = True})

            For Each row In rows
                Fields.Add(row)
            Next
        End Sub

    End Class
End Namespace
