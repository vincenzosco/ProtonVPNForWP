' Profile / setup page.
'
' Windows Phone 8.1 exposes no clipboard API to third-party apps, so the two
' values the user has to type are shown in read-only, selectable text boxes
' rather than behind a "copy" button that could never work.
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Models
Imports Proton_VPN_WP.Services
Imports Proton_VPN_WP.ViewModels
Imports Windows.UI.Xaml
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Navigation

Namespace Views
    Partial Friend NotInheritable Class ProfilePage
        Inherits Page

        ' Windows Phone 8.1 has no documented deep link into the VPN settings page,
        ' so this is attempted and the failure path tells the user where to go.
        Private Const VpnSettingsUri As String = "ms-settings-vpn:"

        Private ReadOnly _viewModel As New ProfileViewModel()

        Public Sub New()
            InitializeComponent()
            DataContext = _viewModel
            FieldList.ItemsSource = _viewModel.Fields
        End Sub

        Protected Overrides Async Sub OnNavigatedTo(e As NavigationEventArgs)
            MyBase.OnNavigatedTo(e)
            Await _viewModel.LoadAsync()
            UpdateVisualState()
        End Sub

        Private Sub OnRevealToggled(sender As Object, e As RoutedEventArgs)
            _viewModel.RevealPassword = RevealSwitch.IsOn
            UpdateVisualState()
        End Sub

        Private Async Sub OnOpenSettingsClick(sender As Object, e As RoutedEventArgs)
            Try
                Dim opened As Boolean = Await Windows.System.Launcher.LaunchUriAsync(New Uri(VpnSettingsUri))
                If Not opened Then Throw New InvalidOperationException("no handler")
                _viewModel.NoticeLine = "Opened system settings — look for VPN."
            Catch ex As Exception
                Log.Info("VPN settings deep link unavailable: " & ex.Message)
                _viewModel.NoticeLine = "This build of Windows Phone has no shortcut to the VPN page. Open Settings, then VPN, and add the values above."
            End Try
            UpdateVisualState()
        End Sub

        Private Sub UpdateVisualState()
            LocationText.Text = _viewModel.LocationLine
            BusyRing.IsActive = _viewModel.IsBusy
            StatusText.Text = _viewModel.StatusMessage
            NoticeText.Text = _viewModel.NoticeLine
            NoticeText.Visibility = If(String.IsNullOrEmpty(_viewModel.NoticeLine), Visibility.Collapsed, Visibility.Visible)
            CopyUsernameBox.Text = _viewModel.Username
            CopyPasswordBox.Text = _viewModel.PasswordDisplay
            OpenSettingsButton.IsEnabled = _viewModel.HasProfile
        End Sub

    End Class
End Namespace
