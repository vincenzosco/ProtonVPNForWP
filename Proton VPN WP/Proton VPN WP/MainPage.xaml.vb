' The application shell: owns the navigation frame and the hardware back button.
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Services
Imports Windows.Phone.UI.Input
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Navigation

Partial Public NotInheritable Class MainPage
    Inherits Page

    Private _unsubscribe As Action

    Protected Overrides Sub OnNavigatedTo(e As NavigationEventArgs)
        MyBase.OnNavigatedTo(e)

        AddHandler HardwareButtons.BackPressed, AddressOf OnBackPressed

        ' Start on the dashboard when a session is still on the device, otherwise sign in.
        If RootFrame.Content Is Nothing Then
            If AppServices.Current.IsSignedIn Then
                RootFrame.Navigate(GetType(Views.HomePage))
            Else
                RootFrame.Navigate(GetType(Views.LoginPage))
            End If
        End If
    End Sub

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
