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
        InitializeComponent()
        AddHandler Me.UnhandledException, AddressOf OnUnhandledException
    End Sub

    Protected Overrides Sub OnLaunched(args As LaunchActivatedEventArgs)
        Dim root As New MainPage()
        Window.Current.Content = root
        Window.Current.Activate()

        ' Started here, awaited by the shell before it decides which page to show.
        ' Showing the UI first keeps startup responsive.
        _sessionRestore = RestoreSessionAsync()
    End Sub

    Private Async Function RestoreSessionAsync() As Task
        Try
            Dim session As Proton_VPN_WP.Models.ProtonSession = Await AppServices.Current.Auth.RestoreAsync()
            If session IsNot Nothing Then
                AppServices.Current.Session = session
                Log.Info("Restored a stored Proton session.")
            End If
        Catch ex As Exception
            Log.Warn("Could not restore the stored session: " & ex.Message)
        End Try
    End Function

    Private Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        Log.Error("Unhandled exception: " & e.Message)
    End Sub

End Class
