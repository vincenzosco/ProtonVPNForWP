' Sign-in page.
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Services
Imports Proton_VPN_WP.ViewModels
Imports Windows.UI.Xaml
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Navigation

Namespace Views
    Partial Friend NotInheritable Class LoginPage
        Inherits Page

        Private ReadOnly _viewModel As New LoginViewModel()

        Public Sub New()
            InitializeComponent()
            DataContext = _viewModel
        End Sub

        Protected Overrides Sub OnNavigatedTo(e As NavigationEventArgs)
            MyBase.OnNavigatedTo(e)
            _viewModel.IsBusy = False
            If Not String.IsNullOrEmpty(AppServices.Current.Username) Then
                _viewModel.Username = AppServices.Current.Username
                UsernameBox.Text = _viewModel.Username
            End If
            UpdateVisualState()
        End Sub

        Private Async Sub OnSignInClick(sender As Object, e As RoutedEventArgs)
            ' The password is read here and handed straight to the view model; it is
            ' never assigned to a property and never logged.
            Dim password As String = PasswordEntry.Password
            Dim code As String = If(TwoFactorBox.Text, String.Empty)

            UpdateVisualState()
            Dim signedIn As Boolean = Await _viewModel.SignInAsync(password, code)
            UpdateVisualState()

            If signedIn Then
                PasswordEntry.Password = String.Empty
                TwoFactorBox.Text = String.Empty
                Frame.Navigate(GetType(HomePage))
            End If
        End Sub

        Private Sub OnOfflineClick(sender As Object, e As RoutedEventArgs)
            _viewModel.IsOfflineMode = True
            AppServices.Current.Credentials = Nothing
            Frame.Navigate(GetType(HomePage))
        End Sub

        Private Sub UpdateVisualState()
            BusyRing.IsActive = _viewModel.IsBusy
            StatusText.Text = _viewModel.StatusMessage
            TwoFactorPanel.Visibility = If(_viewModel.NeedsTwoFactor, Visibility.Visible, Visibility.Collapsed)
            ErrorText.Text = _viewModel.ErrorMessage
            ErrorText.Visibility = If(_viewModel.HasError, Visibility.Visible, Visibility.Collapsed)
            SignInButton.IsEnabled = Not _viewModel.IsBusy
        End Sub

    End Class
End Namespace
