' Application bootstrap.
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Services
Imports Windows.ApplicationModel.Activation
Imports Windows.UI.Xaml

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

        Dim root As New MainPage()
        Window.Current.Content = root
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

End Class
