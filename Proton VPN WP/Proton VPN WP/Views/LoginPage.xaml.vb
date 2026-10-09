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

        ' True only while this page has a sign-in in flight. IsBusy flips inside
        ' SignInAsync -- after the pre-await UpdateVisualState -- so a button keyed on
        ' IsBusy alone stayed armed for the whole round trip: thirteen presses started
        ' thirteen attempts, all of them over the same pending second-factor state.
        Private _signingIn As Boolean

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
            If _signingIn Then Return

            ' The password is read here and handed straight to the view model; it is
            ' never assigned to a property and never logged.
            Dim password As String = PasswordEntry.Password
            Dim code As String = If(TwoFactorBox.Text, String.Empty)

            ' The box is the only place a typed user name ever lives: the view model's
            ' Username is seeded from a stored session and nowhere else, so without this
            ' copy a filled-in form reached the service with an empty user name and was
            ' rejected with "Enter your Proton username and password."
            _viewModel.Username = UsernameBox.Text

            ' Raised before the await and lowered after it: the wait is exactly the
            ' window in which a second press used to start a second attempt.
            _signingIn = True
            UpdateVisualState()
            Try
                Dim signedIn As Boolean = Await _viewModel.SignInAsync(password, code)
                If signedIn Then
                    PasswordEntry.Password = String.Empty
                    TwoFactorBox.Text = String.Empty
                    Frame.Navigate(GetType(HomePage))
                End If
            Finally
                _signingIn = False
                UpdateVisualState()
            End Try
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
            SignInButton.IsEnabled = Not _viewModel.IsBusy AndAlso Not _signingIn
            ' The door out of the page is disarmed too: leaving mid-attempt would build
            ' a fresh LoginPage whose own flag is clear, and the only way back to a
            ' second attempt is through a page that still has the first one running.
            OfflineButton.IsEnabled = Not _signingIn
        End Sub

    End Class
End Namespace
