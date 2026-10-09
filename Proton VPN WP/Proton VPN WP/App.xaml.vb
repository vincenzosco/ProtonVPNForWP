' Application bootstrap.
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Services
Imports Windows.ApplicationModel.Activation
Imports Windows.UI.Xaml
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Navigation

Partial Public NotInheritable Class App
    Inherits Application

    ''' <summary>
    ''' The in-flight session restore.
    '''
    ''' The shell has to sequence on this: choosing between the sign-in page and the
    ''' dashboard means reading IsSignedIn, and reading it before the vault has been
    ''' read always yields False. Previously the restore was fire-and-forget, so a
    ''' stored session could never win.
    ''' </summary>
    Private Shared _sessionRestore As Task = Nothing

    Friend Shared ReadOnly Property SessionRestore As Task
        Get
            Return _sessionRestore
        End Get
    End Property

    Public Sub New()
        Log.Info("startup: app constructed")
        InitializeComponent()
        AddHandler Me.UnhandledException, AddressOf OnUnhandledException
    End Sub

    Protected Overrides Sub OnLaunched(args As LaunchActivatedEventArgs)
        ' First line of every session: where the log is. Without it the file is only
        ' discoverable with a debugger attached, which is exactly what is missing
        ' when the app will not start.
        Log.Info("startup: log file " & LogStore.FilePath())

        ' The shell page has to be navigated to, not merely assigned: Page.OnNavigatedTo
        ' is raised by the Frame that navigates, so a page dropped straight into
        ' Window.Current.Content never receives it. On the device that left RootFrame
        ' empty forever -- the app rendered and showed nothing, and the same dead
        ' handler was the only place the hardware back button got registered.
        Dim shellHost As New Frame()
        Window.Current.Content = shellHost
        ' A page that throws while loading raises this instead of killing the process
        ' with nothing in the log naming it.
        AddHandler shellHost.NavigationFailed, AddressOf OnNavigationFailed
        shellHost.Navigate(GetType(MainPage))
        Window.Current.Activate()
        Log.Info("startup: shell activated; restoring the stored session")

        ' Started here, awaited with a budget by the shell before it decides which
        ' page to show.
        _sessionRestore = RestoreSessionAsync()
    End Sub

    Private Async Function RestoreSessionAsync() As Task
        ' Breadcrumbs on both sides of the await: whichever line is last in the file
        ' names the step that stalled.
        Log.Info("restore: begin")
        Try
            Dim session As Proton_VPN_WP.Models.ProtonSession = Await AppServices.Current.Auth.RestoreAsync()
            If session IsNot Nothing Then
                AppServices.Current.Session = session
                Log.Info("restore: stored session restored")
            Else
                Log.Info("restore: no stored session")
            End If
        Catch ex As Exception
            Log.Warn("restore: failed: " & ex.Message)
        End Try
        Log.Info("restore: end")
    End Function

    Private Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        Log.Error("Unhandled exception: " & e.Message)
    End Sub

    Private Sub OnNavigationFailed(sender As Object, e As NavigationFailedEventArgs)
        ' The page's own bug stays the cause to fix; this only makes the failure
        ' legible, which an unhandled NavigationFailed is not.
        Log.Error("startup: navigation to " & e.SourcePageType.Name & " failed: " & e.Exception.Message)
    End Sub

End Class
