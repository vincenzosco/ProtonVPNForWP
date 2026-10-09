' Server browser page.
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Models
Imports Proton_VPN_WP.Services
Imports Proton_VPN_WP.ViewModels
Imports Windows.UI.Xaml
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Navigation

Namespace Views
    Partial Friend NotInheritable Class ServersPage
        Inherits Page

        Private ReadOnly _viewModel As New ServersViewModel()

        Public Sub New()
            InitializeComponent()
            DataContext = _viewModel
            ServerList.ItemsSource = _viewModel.Servers
        End Sub

        Protected Overrides Async Sub OnNavigatedTo(e As NavigationEventArgs)
            MyBase.OnNavigatedTo(e)
            Dim result = Await _viewModel.LoadAsync(forceRefresh:=False)
            UpdateVisualState()
        End Sub

        Private Sub OnSearchTextChanged(sender As Object, e As TextChangedEventArgs)
            _viewModel.SearchText = SearchBox.Text
            UpdateVisualState()
        End Sub

        Private Sub OnFreeOnlyToggled(sender As Object, e As RoutedEventArgs)
            _viewModel.OnlyFree = FreeOnlySwitch.IsOn
            UpdateVisualState()
        End Sub

        Private Sub OnServerClick(sender As Object, e As ItemClickEventArgs)
            Dim server As ProtonLogical = TryCast(e.ClickedItem, ProtonLogical)
            If server Is Nothing Then Return
            HomePage.RememberSelection(server)
            Frame.Navigate(GetType(ServerDetailPage), server)
        End Sub

        Private Sub UpdateVisualState()
            SummaryText.Text = _viewModel.Summary
            BusyRing.IsActive = _viewModel.IsBusy
            EmptyText.Visibility = If(_viewModel.IsBusy OrElse _viewModel.Servers.Count > 0, Visibility.Collapsed, Visibility.Visible)
        End Sub

    End Class
End Namespace
