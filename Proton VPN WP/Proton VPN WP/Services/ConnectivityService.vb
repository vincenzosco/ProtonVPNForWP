' Network awareness.
'
' The app cannot query a tunnel it does not own, so the honest signals available on
' WP8.1 are: is there a usable internet profile, and how long does a TCP handshake
' to the chosen server take. Both are surfaced as-is, never as "connected".
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Threading.Tasks
Imports Windows.Networking
Imports Windows.Networking.Connectivity
Imports Windows.Networking.Sockets

Namespace Services
    Friend NotInheritable Class ConnectivityService

        Private Const DefaultVpnPort As Integer = 443

        Private Sub New()
        End Sub

        ''' <summary>True when a non-metered-or-metered profile reports internet access.</summary>
        Friend Shared Function HasInternetAccess() As Boolean
            Try
                Dim profile As ConnectionProfile = NetworkInformation.GetInternetConnectionProfile()
                If profile Is Nothing Then Return False
                Dim level As NetworkConnectivityLevel = profile.GetNetworkConnectivityLevel()
                Return level = NetworkConnectivityLevel.InternetAccess
            Catch ex As Exception
                Log.Warn("Could not read the connection profile: " & ex.Message)
                Return False
            End Try
        End Function

        Friend Shared Function ActiveNetworkName() As String
            Try
                Dim profile As ConnectionProfile = NetworkInformation.GetInternetConnectionProfile()
                If profile Is Nothing Then Return "Offline"
                Return profile.ProfileName
            Catch
                Return "Unknown"
            End Try
        End Function

        ''' <summary>
        ''' TCP connect time to host:port in milliseconds, or -1 when unreachable.
        ''' The socket is always disposed, including on the timeout path.
        ''' </summary>
        Friend Shared Async Function MeasureLatencyAsync(host As String, Optional port As Integer = DefaultVpnPort,
                                                         Optional timeoutMs As Integer = 4000) As Task(Of Integer)
            If String.IsNullOrEmpty(host) Then Return -1

            Dim socket As New StreamSocket()
            Try
                Dim stopwatch As Stopwatch = Stopwatch.StartNew()
                Dim connectTask As Task = socket.ConnectAsync(New HostName(host), port.ToString()).AsTask()
                Dim finished As Task = Await Task.WhenAny(connectTask, Task.Delay(timeoutMs))

                If Not Object.ReferenceEquals(finished, connectTask) Then
                    ' Timed out. Disposing the socket below aborts the pending connect.
                    Return -1
                End If

                Await connectTask
                stopwatch.Stop()
                Return CInt(stopwatch.ElapsedMilliseconds)
            Catch ex As Exception
                Log.Info("Latency probe to " & host & " failed: " & ex.Message)
                Return -1
            Finally
                socket.Dispose()
            End Try
        End Function

        ''' <summary>Registers for connectivity changes and returns an unsubscribe action.</summary>
        Friend Shared Function ObserveNetworkChanges(handler As NetworkStatusChangedEventHandler) As Action
            ' VB has no += for events declared on another type, so AddHandler is the
            ' only way to subscribe to this static event.
            AddHandler NetworkInformation.NetworkStatusChanged, handler
            Return Sub()
                       RemoveHandler NetworkInformation.NetworkStatusChanged, handler
                   End Sub
        End Function

        Friend Shared Function Summarise() As IList(Of String)
            Dim lines As New List(Of String)()
            Try
                lines.Add("Internet: " & If(HasInternetAccess(), "yes", "no"))
                lines.Add("Network: " & ActiveNetworkName())
                Dim profile As ConnectionProfile = NetworkInformation.GetInternetConnectionProfile()
                If profile IsNot Nothing Then
                    Dim cost As ConnectionCost = profile.GetConnectionCost()
                    If cost IsNot Nothing Then
                        lines.Add("Roaming: " & If(cost.Roaming, "yes", "no"))
                        lines.Add("Metered: " & If(cost.NetworkCostType <> NetworkCostType.Unrestricted, "yes", "no"))
                    End If
                End If
            Catch ex As Exception
                lines.Add("Network details unavailable.")
            End Try
            Return lines
        End Function

    End Class
End Namespace
