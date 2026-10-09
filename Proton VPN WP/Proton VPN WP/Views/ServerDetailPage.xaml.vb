' Single-server detail page.
Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Models
Imports Proton_VPN_WP.Services
Imports Windows.UI.Xaml
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Navigation

Namespace Views
    Partial Friend NotInheritable Class ServerDetailPage
        Inherits Page

        Private _server As ProtonLogical

        Public Sub New()
            InitializeComponent()
        End Sub

        Protected Overrides Sub OnNavigatedTo(e As NavigationEventArgs)
            MyBase.OnNavigatedTo(e)

            Dim incoming As ProtonLogical = TryCast(e.Parameter, ProtonLogical)
            If incoming Is Nothing Then
                ' A tile or a deep link can land here without a parameter: fall back to
                ' whatever the user last chose.
                Dim settings As AppSettings = AppServices.Current.Settings.Load()
                incoming = AppServices.Current.Catalog.FindByName(settings.LastServerName)
            End If

            If incoming Is Nothing Then
                NameText.Text = "No server"
                LocationText.Text = "Open the Servers page and pick one."
                UseButton.IsEnabled = False
                MeasureButton.IsEnabled = False
                Return
            End If

            _server = incoming
            Show(_server)
        End Sub

        Private Sub Show(server As ProtonLogical)
            NameText.Text = server.Name
            LocationText.Text = server.LocationText
            StatusText.Text = server.StatusText
            LoadText.Text = server.LoadText
            FeatureText.Text = server.FeatureText
            TierText.Text = server.TierText & " tier"
            HostText.Text = "Host name: " & server.HostName
            PhysicalList.ItemsSource = server.Servers

            If Not server.IsAvailable Then
                NoticeText.Text = "Proton reports this server as " & server.StatusText & ". Connecting may fail."
                NoticeText.Visibility = Visibility.Visible
            End If
        End Sub

        Private Sub OnUseClick(sender As Object, e As RoutedEventArgs)
            If _server Is Nothing Then Return
            HomePage.RememberSelection(_server)
            Frame.Navigate(GetType(ProfilePage))
        End Sub

        Private Async Sub OnMeasureClick(sender As Object, e As RoutedEventArgs)
            If _server Is Nothing Then Return

            MeasureButton.IsEnabled = False
            BusyRing.IsActive = True
            LatencyText.Text = "Measuring…"

            Dim host As String = _server.HostName
            Dim milliseconds As Integer = Await ConnectivityService.MeasureLatencyAsync(host)

            BusyRing.IsActive = False
            MeasureButton.IsEnabled = True

            If milliseconds < 0 Then
                LatencyText.Text = "No TCP handshake to " & host & " within the timeout. This is a reachability probe, not a VPN connection."
            Else
                LatencyText.Text = "TCP handshake to " & host & " took " & milliseconds.ToString() & " ms."
            End If
        End Sub

    End Class
End Namespace
