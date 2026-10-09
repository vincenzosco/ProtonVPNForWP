' Application bootstrap.
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Services
Imports Windows.ApplicationModel.Activation
Imports Windows.UI.Xaml

Partial Public NotInheritable Class App
    Inherits Application

    Public Sub New()
        InitializeComponent()
        AddHandler Me.UnhandledException, AddressOf OnUnhandledException
    End Sub

    Protected Overrides Sub OnLaunched(args As LaunchActivatedEventArgs)
        Dim root As New MainPage()
        Window.Current.Content = root
        Window.Current.Activate()

        ' Restore a previously stored session in the background; the shell reads
        ' IsSignedIn when it decides which page to show, so this only ever upgrades
        ' the experience, it never blocks startup.
        RestoreSession()
    End Sub

    Private Async Sub RestoreSession()
        Try
            Dim session As Proton_VPN_WP.Models.ProtonSession = Await AppServices.Current.Auth.RestoreAsync()
            If session IsNot Nothing Then
                AppServices.Current.Session = session
                Log.Info("Restored a stored Proton session.")
            End If
        Catch ex As Exception
            Log.Warn("Could not restore the stored session: " & ex.Message)
        End Try
    End Sub

    Private Sub OnUnhandledException(sender As Object, e As UnhandledExceptionEventArgs)
        Log.Error("Unhandled exception: " & e.Message)
    End Sub

End Class
