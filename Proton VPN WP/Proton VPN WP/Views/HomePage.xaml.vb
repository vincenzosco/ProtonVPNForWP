' Dashboard page.
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Models
Imports Proton_VPN_WP.Services
Imports Proton_VPN_WP.ViewModels
Imports Windows.UI.Xaml
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Navigation

Namespace Views
    Partial Friend NotInheritable Class HomePage
        Inherits Page

        Private ReadOnly _viewModel As New HomeViewModel()
        Private _loaded As Boolean

        Public Sub New()
            InitializeComponent()
            DataContext = _viewModel
        End Sub

        Protected Overrides Async Sub OnNavigatedTo(e As NavigationEventArgs)
            MyBase.OnNavigatedTo(e)
            _viewModel.RefreshAccount()
            UpdateVisualState()

            ' Reloading every time the user comes back keeps the dashboard honest
            ' after they pick a different server.
            Dim result As ApiResult(Of System.Collections.Generic.IList(Of ProtonLogical)) =
                Await _viewModel.InitializeAsync(forceRefresh:=Not _loaded)
            _loaded = True
            UpdateVisualState()
        End Sub

        Private Sub OnQuickConnectClick(sender As Object, e As RoutedEventArgs)
            Dim chosen As ProtonLogical = _viewModel.QuickPick()
            If chosen Is Nothing Then
                _viewModel.NoticeLine = "No server is available in the current list."
                UpdateVisualState()
                Return
            End If
            _viewModel.SelectedServer = chosen
            RememberSelection(chosen)
            UpdateVisualState()
            Frame.Navigate(GetType(ProfilePage))
        End Sub

        Private Sub OnServersClick(sender As Object, e As RoutedEventArgs)
            Frame.Navigate(GetType(ServersPage))
        End Sub

        Private Sub OnProfileClick(sender As Object, e As RoutedEventArgs)
            Frame.Navigate(GetType(ProfilePage))
        End Sub

        Private Sub OnSettingsClick(sender As Object, e As RoutedEventArgs)
            Frame.Navigate(GetType(SettingsPage))
        End Sub

        Private Sub OnAboutClick(sender As Object, e As RoutedEventArgs)
            Frame.Navigate(GetType(AboutPage))
        End Sub

        Friend Shared Sub RememberSelection(server As ProtonLogical)
            If server Is Nothing Then Return
            Dim settings As AppSettings = AppServices.Current.Settings.Load()
            settings.LastServerName = server.Name
            settings.LastServerDomain = server.HostName
            settings.LastCountryCode = server.CountryCode
            AppServices.Current.Settings.Save(settings)
        End Sub

        Private Sub UpdateVisualState()
            AccountText.Text = _viewModel.AccountLine
            NetworkText.Text = _viewModel.NetworkLine
            CatalogueText.Text = _viewModel.CatalogueLine
            SelectionText.Text = _viewModel.SelectionLine
            NoticeText.Text = _viewModel.NoticeLine
            NoticeText.Visibility = If(_viewModel.HasNotice, Visibility.Visible, Visibility.Collapsed)
            BusyRing.IsActive = _viewModel.IsBusy
            StatusText.Text = _viewModel.StatusMessage
            QuickConnectButton.IsEnabled = Not _viewModel.IsBusy
        End Sub

    End Class
End Namespace
