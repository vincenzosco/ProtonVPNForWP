' The application shell: owns the navigation frame and the hardware back button.
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Services
Imports Windows.Phone.UI.Input
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Navigation

Partial Public NotInheritable Class MainPage
    Inherits Page

    Private _unsubscribe As Action

    ''' <summary>
    ''' How long the shell waits for the stored session before showing a page anyway.
    ''' The vault read is local, so this is normally a few milliseconds; the budget
    ''' exists so that a wedged read cannot leave the app on the splash screen.
    ''' </summary>
    Private Shared ReadOnly StartupBudget As TimeSpan = TimeSpan.FromSeconds(2)

    Protected Overrides Async Sub OnNavigatedTo(e As NavigationEventArgs)
        MyBase.OnNavigatedTo(e)

        ' Registered before the await so an early back press is not missed.
        AddHandler HardwareButtons.BackPressed, AddressOf OnBackPressed

        If RootFrame.Content Is Nothing Then
            ' The stored session arrives asynchronously, and deciding before it lands
            ' would send a signed-in user back to the sign-in page. Waiting for it
            ' without a bound, though, is what kept the app from ever drawing a page.
            Dim landed As Boolean = Await WaitForRestoreAsync()
            If landed Then
                Log.Info("startup: the stored session has been decided")
            Else
                Log.Warn("startup: the restore exceeded its budget; continuing without it")
            End If

            ' Start on the dashboard when a session is still on the device, otherwise sign in.
            If RootFrame.Content Is Nothing Then
                If AppServices.Current.IsSignedIn Then
                    RootFrame.Navigate(GetType(Views.HomePage))
                Else
                    RootFrame.Navigate(GetType(Views.LoginPage))
                End If
            End If

            Log.Info("startup: first page shown")

            ' The UI is on screen now, so anything needing the network goes behind it.
            Dim refresh As Task = AppServices.Current.Auth.RefreshInBackgroundAsync()
        End If
    End Sub

    ''' <summary>
    ''' Waits for the stored session for at most <see cref="StartupBudget"/>, and says
    ''' whether it landed in time.
    ''' </summary>
    Private Shared Async Function WaitForRestoreAsync() As Task(Of Boolean)
        Dim restore As Task = App.SessionRestore
        If restore Is Nothing Then Return True

        Dim finished As Task = Await Task.WhenAny(restore, Task.Delay(StartupBudget))
        Return finished Is restore
    End Function

    Protected Overrides Sub OnNavigatedFrom(e As NavigationEventArgs)
        MyBase.OnNavigatedFrom(e)
        RemoveHandler HardwareButtons.BackPressed, AddressOf OnBackPressed
        If _unsubscribe IsNot Nothing Then
            _unsubscribe.Invoke()
            _unsubscribe = Nothing
        End If
    End Sub

    Private Sub OnBackPressed(sender As Object, e As BackPressedEventArgs)
        If RootFrame.CanGoBack Then
            e.Handled = True
            RootFrame.GoBack()
        End If
    End Sub

End Class
