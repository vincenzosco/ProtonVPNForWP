' Settings page: preferences, credentials, catalogue maintenance and diagnostics.
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Models
Imports Proton_VPN_WP.Services
Imports Windows.UI.Xaml
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Navigation

Namespace Views
    Partial Friend NotInheritable Class SettingsPage
        Inherits Page

        ' Set while loading controls from storage, so the change handlers do not
        ' write the value straight back (and do not trigger a catalogue refresh).
        Private _loading As Boolean

        Public Sub New()
            InitializeComponent()
        End Sub

        Protected Overrides Async Sub OnNavigatedTo(e As NavigationEventArgs)
            MyBase.OnNavigatedTo(e)
            LoadFromSettings()
            RefreshDiagnostics()
            Await LoadStoredCredentialsAsync()
        End Sub

        ''' <summary>
        ''' Fills the credential boxes from the vault.
        '''
        ''' This is not cosmetic: the page saves both fields whenever either is
        ''' non-empty, so leaving the boxes blank meant that correcting just the user
        ''' name wrote an empty password and destroyed the stored one.
        ''' </summary>
        Private Async Function LoadStoredCredentialsAsync() As Task
            Dim stored As VpnCredentials = Await AppServices.Current.Vault.LoadCredentialsAsync()
            If stored Is Nothing Then Return

            _loading = True
            Try
                VpnUserBox.Text = If(stored.Username, String.Empty)
                VpnPasswordBox.Password = If(stored.Password, String.Empty)
            Finally
                _loading = False
            End Try
        End Function

        Private Sub LoadFromSettings()
            _loading = True
            Try
                Dim settings As AppSettings = AppServices.Current.Settings.Load()

                IkeRadio.IsChecked = (settings.PreferredProtocol = ProfileKind.IkeV2)
                OpenVpnRadio.IsChecked = (settings.PreferredProtocol = ProfileKind.OpenVpn)
                AutoRefreshSwitch.IsOn = settings.AutoRefreshCatalogue
                FreeOnlySwitch.IsOn = settings.OnlyFreeServers

                UpdateAccountLabels()
                CatalogueText.Text = "Source: " & HomeViewModelDescribe(AppServices.Current.Catalog.Source) &
                                     " · " & AppServices.Current.Catalog.Count.ToString() & " servers"
            Finally
                _loading = False
            End Try
        End Sub

        Private Sub UpdateAccountLabels()
            If AppServices.Current.IsSignedIn Then
                AccountText.Text = "Signed in as " & AppServices.Current.Username
                SignInButton.Visibility = Visibility.Collapsed
                SignOutButton.Visibility = Visibility.Visible
            Else
                AccountText.Text = "Not signed in. The app still works with the offline server list."
                SignInButton.Visibility = Visibility.Visible
                SignOutButton.Visibility = Visibility.Collapsed
            End If
        End Sub

        Private Shared Function HomeViewModelDescribe(source As CatalogueSource) As String
            Select Case source
                Case CatalogueSource.Api : Return "live from Proton"
                Case CatalogueSource.Cache : Return "cached"
                Case CatalogueSource.Bundled : Return "offline bundle"
                Case Else : Return "not loaded"
            End Select
        End Function

        Private Sub Persist(settings As AppSettings)
            AppServices.Current.Settings.Save(settings)
        End Sub

        Private Sub OnProtocolChanged(sender As Object, e As RoutedEventArgs)
            If _loading Then Return
            Dim settings As AppSettings = AppServices.Current.Settings.Load()
            settings.PreferredProtocol = If(OpenVpnRadio.IsChecked = True, ProfileKind.OpenVpn, ProfileKind.IkeV2)
            Persist(settings)
            StatusText.Text = "Default protocol set to " & settings.PreferredProtocol.ToString() & "."
        End Sub

        Private Sub OnAutoRefreshToggled(sender As Object, e As RoutedEventArgs)
            If _loading Then Return
            Dim settings As AppSettings = AppServices.Current.Settings.Load()
            settings.AutoRefreshCatalogue = AutoRefreshSwitch.IsOn
            Persist(settings)
        End Sub

        Private Sub OnFreeOnlyToggled(sender As Object, e As RoutedEventArgs)
            If _loading Then Return
            Dim settings As AppSettings = AppServices.Current.Settings.Load()
            settings.OnlyFreeServers = FreeOnlySwitch.IsOn
            Persist(settings)
        End Sub

        Private Sub OnCredentialChanged(sender As Object, e As TextChangedEventArgs)
            If _loading Then Return
            StatusText.Text = "Credentials are saved when you leave this page."
        End Sub

        Private Sub OnCredentialPasswordChanged(sender As Object, e As RoutedEventArgs)
            If _loading Then Return
            StatusText.Text = "Credentials are saved when you leave this page."
        End Sub

        Protected Overrides Async Sub OnNavigatedFrom(e As NavigationEventArgs)
            MyBase.OnNavigatedFrom(e)
            Await SaveCredentialsAsync()
        End Sub

        Private Async Function SaveCredentialsAsync() As Task
            Dim username As String = If(VpnUserBox.Text, "").Trim()
            Dim password As String = If(VpnPasswordBox.Password, "")

            If username.Length = 0 AndAlso password.Length = 0 Then Return

            Dim credentials As New VpnCredentials()
            credentials.Username = username
            credentials.Password = password
            credentials.Source = CredentialSource.Manual

            AppServices.Current.Credentials = credentials
            Await AppServices.Current.Vault.StoreCredentialsAsync(credentials)
        End Function

        Private Sub OnSignInClick(sender As Object, e As RoutedEventArgs)
            Frame.Navigate(GetType(LoginPage))
        End Sub

        Private Sub OnSignOutClick(sender As Object, e As RoutedEventArgs)
            AppServices.Current.SignOut()
            UpdateAccountLabels()
            StatusText.Text = "Signed out. Stored tokens and credentials were erased."
        End Sub

        Private Async Sub OnRefreshClick(sender As Object, e As RoutedEventArgs)
            RefreshButton.IsEnabled = False
            BusyRing.IsActive = True
            StatusText.Text = "Contacting Proton…"

            Dim result As ApiResult(Of System.Collections.Generic.IList(Of ProtonLogical)) =
                Await AppServices.Current.Catalog.LoadAsync(forceRefresh:=True)

            BusyRing.IsActive = False
            RefreshButton.IsEnabled = True
            CatalogueText.Text = "Source: " & HomeViewModelDescribe(AppServices.Current.Catalog.Source) &
                                 " · " & AppServices.Current.Catalog.Count.ToString() & " servers"
            StatusText.Text = If(String.IsNullOrEmpty(result.Message),
                                 "Server list refreshed.",
                                 result.Message)
        End Sub

        Private Async Sub OnClearCacheClick(sender As Object, e As RoutedEventArgs)
            Await AppServices.Current.Catalog.ResetAsync()
            CatalogueText.Text = "Source: " & HomeViewModelDescribe(AppServices.Current.Catalog.Source) &
                                 " · " & AppServices.Current.Catalog.Count.ToString() & " servers"
            StatusText.Text = "Cached server list cleared."
        End Sub

        Private Sub OnResetClick(sender As Object, e As RoutedEventArgs)
            AppServices.Current.Settings.Reset()
            AppServices.Current.Vault.Clear()
            AppServices.Current.SignOut()
            _loading = True
            VpnUserBox.Text = ""
            VpnPasswordBox.Password = ""
            _loading = False
            LoadFromSettings()
            StatusText.Text = "All app data erased."
        End Sub

        Private Sub RefreshDiagnostics()
            DiagnosticsList.ItemsSource = ConnectivityService.Summarise()
        End Sub

    End Class
End Namespace
