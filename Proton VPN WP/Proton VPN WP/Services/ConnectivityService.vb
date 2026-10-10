' Network awareness.
'
' The app cannot query a tunnel it does not own, so the honest signals available on
' WP8.1 are: is there a usable internet profile, and how long does a TCP handshake
' to the chosen server take. Both are surfaced as-is, never as "connected".
Imports System.Collections.Generic
Imports System.Diagnostics
Imports System.Threading
Imports System.Threading.Tasks
Imports Windows.Networking
Imports Windows.Networking.Connectivity
Imports Windows.Networking.Sockets
Imports Windows.Web.Http

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

        ''' <summary>How long one probe layer may take before it is reported as unanswered.</summary>
        Private Const DiagnoseProbeTimeoutMs As Integer = 5000

        ''' <summary>The port Proton's HTTPS endpoint and its VPN profile share.</summary>
        Private Const DiagnosePort As Integer = 443

        ''' <summary>
        ''' The second opinion in the probe set: a host that is not Proton, reached over
        ''' plain HTTP. Without it, "this device has no internet" and "only Proton is
        ''' unreachable from here" produce the same failed request with no way to tell
        ''' them apart -- and only one of those is this app's problem.
        ''' </summary>
        Private Const ControlUrl As String = "http://www.msftconnecttest.com/connecttest.txt"

        ''' <summary>
        ''' The control for the handshake layer, and a different one from the HTTP
        ''' control: a host that is not Proton and whose certificate chain is long enough
        ''' that a successful handshake also proves the connection can carry more than one
        ''' segment. A handshake that succeeds here and fails against Proton is a refusal
        ''' of this client's protocol set, not a transport this device cannot use.
        ''' </summary>
        Private Const ControlTlsHost As String = "www.microsoft.com"

        ''' <summary>Floor between two diagnoses, so a retry loop cannot flood the log.</summary>
        Private Const DiagnoseMinIntervalSeconds As Integer = 20

        Private Shared ReadOnly DiagnoseGate As New Object()
        Private Shared _diagnoseBusy As Boolean
        Private Shared _lastDiagnoseUtc As DateTime = DateTime.MinValue

        ''' <summary>
        ''' Names the layer that refused a request, one line each, into the log.
        '''
        ''' Both device transcripts -- 2026-10-09 and 2026-10-10 -- end in the same
        ''' line: "Network request failed: Exception Exception from HRESULT:
        ''' 0x80072EFD". A name that did not resolve, a profile with no internet, a
        ''' refused connection and a host that is merely unreachable from here all read
        ''' exactly like that, and only one of the four is this app's bug. The layers
        ''' separate them, and the control host says whether the internet is there at
        ''' all.
        '''
        ''' Log-only and bounded: it changes nothing the caller returns, it runs once at
        ''' a time and at most once per <see cref="DiagnoseMinIntervalSeconds"/>, and no
        ''' caller with something to show waits for it.
        ''' </summary>
        Friend Shared Async Function DiagnoseAsync(reason As String, protonUrl As String) As Task
            SyncLock DiagnoseGate
                If _diagnoseBusy Then Return
                If DateTime.UtcNow - _lastDiagnoseUtc < TimeSpan.FromSeconds(DiagnoseMinIntervalSeconds) Then Return
                ' Raised before the first Await, and this lock is never held across one:
                ' two failures arriving together must not both walk the layers.
                _diagnoseBusy = True
                _lastDiagnoseUtc = DateTime.UtcNow
            End SyncLock

            Try
                Log.Warn("netdiag: " & reason)
                For Each line As String In Summarise()
                    Log.Info("netdiag: profile " & line)
                Next

                Dim host As String = New Uri(protonUrl).Host
                Await ProbeDnsAsync(host)
                Await ProbeTcpAsync(host, DiagnosePort)
                ' The stack the app actually uses, first, because that is the line that
                ' answers "can this device reach Proton".
                Await ProbeManagedTlsAsync(host, DiagnosePort)
                ' Then the platform's own stack, which cannot: keeping it in the log is
                ' what documents why the app carries a TLS client of its own. The TLS 1.1
                ' probe was dropped here -- two platform protocols always fail together,
                ' and each probe can cost the full timeout.
                Await ProbeTlsAsync(host, DiagnosePort, SocketProtectionLevel.Tls12, "tls12 platform")
                Await ProbeTlsAsync(ControlTlsHost, DiagnosePort, SocketProtectionLevel.Tls12, "tls12 control")
                Await ProbeHttpAsync("control", ControlUrl)
                Await ProbeHttpAsync("proton", protonUrl)
            Catch ex As Exception
                ' This runs on a path that has already failed once: it must not become a
                ' second failure, so nothing in here is allowed to escape.
                Log.Warn("netdiag: the diagnosis itself failed: " & DescribeSocketFailure(ex))
            Finally
                SyncLock DiagnoseGate
                    _diagnoseBusy = False
                End SyncLock
            End Try
        End Function

        ''' <summary>
        ''' DNS on its own. This resolves without connecting, which is what tells a name
        ''' that will not resolve apart from a connection that was refused.
        ''' </summary>
        Private Shared Async Function ProbeDnsAsync(host As String) As Task
            Try
                Dim resolved As Task(Of IReadOnlyList(Of EndpointPair)) =
                    DatagramSocket.GetEndpointPairsAsync(New HostName(host), DiagnosePort.ToString()).AsTask()
                Dim finished As Task = Await Task.WhenAny(resolved, Task.Delay(DiagnoseProbeTimeoutMs))

                If Not Object.ReferenceEquals(finished, resolved) Then
                    Log.Warn("netdiag: dns " & host & " -> no answer within " &
                             DiagnoseProbeTimeoutMs.ToString() & " ms")
                    Return
                End If

                Dim endpoints As IReadOnlyList(Of EndpointPair) = Await resolved
                If endpoints Is Nothing OrElse endpoints.Count = 0 Then
                    Log.Warn("netdiag: dns " & host & " -> the name resolved to no address")
                    Return
                End If
                Log.Info("netdiag: dns " & host & " -> " & endpoints(0).RemoteHostName.DisplayName)
            Catch ex As Exception
                Log.Warn("netdiag: dns " & host & " failed: " & DescribeSocketFailure(ex))
            End Try
        End Function

        ''' <summary>TCP on its own, so a refused or unreachable port is named as such.</summary>
        Private Shared Async Function ProbeTcpAsync(host As String, port As Integer) As Task
            Dim socket As New StreamSocket()
            Try
                Dim stopwatch As Stopwatch = Stopwatch.StartNew()
                Dim connectTask As Task = socket.ConnectAsync(New HostName(host), port.ToString()).AsTask()
                Dim finished As Task = Await Task.WhenAny(connectTask, Task.Delay(DiagnoseProbeTimeoutMs))

                If Not Object.ReferenceEquals(finished, connectTask) Then
                    Log.Warn("netdiag: tcp " & host & ":" & port.ToString() & " -> no answer within " &
                             DiagnoseProbeTimeoutMs.ToString() & " ms")
                    Return
                End If

                Await connectTask
                stopwatch.Stop()
                Log.Info("netdiag: tcp " & host & ":" & port.ToString() & " -> connected in " &
                         stopwatch.ElapsedMilliseconds.ToString() & " ms")
            Catch ex As Exception
                Log.Warn("netdiag: tcp " & host & ":" & port.ToString() & " failed: " & DescribeSocketFailure(ex))
            Finally
                socket.Dispose()
            End Try
        End Function

        ''' <summary>
        ''' The handshake through the app's own TLS 1.3 client, which is the stack
        ''' every request actually uses. This is the layer the sign-in path depends on,
        ''' so it is probed before the platform's.
        '''
        ''' Bounded twice -- the TCP connect and the handshake separately -- because a
        ''' connection that opens and then goes quiet is the failure that a probe most
        ''' needs to report and least wants to wait for.
        ''' </summary>
        Private Shared Async Function ProbeManagedTlsAsync(host As String, port As Integer) As Task
            Dim stopwatch As Stopwatch = Stopwatch.StartNew()
            Dim transport As Tls.SocketTlsTransport = Nothing
            Try
                Dim connectTask As Task(Of Tls.SocketTlsTransport) = Tls.SocketTlsTransport.ConnectAsync(host, port)
                Dim connected As Task = Await Task.WhenAny(connectTask, Task.Delay(DiagnoseProbeTimeoutMs))
                If Not Object.ReferenceEquals(connected, connectTask) Then
                    Log.Warn("netdiag: tls tls13 (managed) " & host & " -> no answer within " &
                             DiagnoseProbeTimeoutMs.ToString() & " ms")
                    Return
                End If
                transport = Await connectTask

                Dim handshake As Task(Of Tls.TlsClient) = Tls.TlsClient.ConnectAsync(transport, New SystemRandomSource(), host)
                Dim finished As Task = Await Task.WhenAny(handshake, Task.Delay(DiagnoseProbeTimeoutMs))
                If Not Object.ReferenceEquals(finished, handshake) Then
                    Log.Warn("netdiag: tls tls13 (managed) " & host & " -> no answer within " &
                             DiagnoseProbeTimeoutMs.ToString() & " ms")
                    Return
                End If

                Dim client As Tls.TlsClient = Await handshake
                stopwatch.Stop()
                Log.Info("netdiag: tls tls13 (managed) " & host & " -> handshake in " &
                         stopwatch.ElapsedMilliseconds.ToString() & " ms, suite 0x" &
                         client.NegotiatedSuite.ToString("x4"))
                client.Close()
            Catch ex As Exception
                ' The message of a failed handshake is the finding -- "the certificate
                ' chain is not signed correctly", an alert code, a tag mismatch.
                Log.Warn("netdiag: tls tls13 (managed) " & host & " failed: " & ex.Message)
            Finally
                If transport IsNot Nothing Then transport.Close()
            End Try
        End Function

        ''' <summary>
        ''' The handshake on its own, at one named protocol, through the platform's
        ''' Schannel stack.
        '''
        ''' This is the layer that a transport failure hides. The emulator run of
        ''' 2026-10-10 connected TCP to Proton in 71 ms and then failed the HTTPS
        ''' request with the same "cannot connect" a dead network produces, because
        ''' WP8.1's HTTP stack offers the operating system's default protocol set and
        ''' the server closes the connection when it accepts none of them. Naming the
        ''' protocol turns that into a finding instead of a guess.
        ''' </summary>
        Private Shared Async Function ProbeTlsAsync(host As String, port As Integer,
                                                    level As SocketProtectionLevel, label As String) As Task
            Dim socket As New StreamSocket()
            Try
                Dim stopwatch As Stopwatch = Stopwatch.StartNew()
                Dim connectTask As Task = socket.ConnectAsync(New HostName(host), port.ToString(), level).AsTask()
                Dim finished As Task = Await Task.WhenAny(connectTask, Task.Delay(DiagnoseProbeTimeoutMs))

                If Not Object.ReferenceEquals(finished, connectTask) Then
                    Log.Warn("netdiag: tls " & label & " " & host & " -> no answer within " &
                             DiagnoseProbeTimeoutMs.ToString() & " ms")
                    Return
                End If

                Await connectTask
                stopwatch.Stop()
                Log.Info("netdiag: tls " & label & " " & host & " -> handshake in " &
                         stopwatch.ElapsedMilliseconds.ToString() & " ms")
            Catch ex As Exception
                Log.Warn("netdiag: tls " & label & " " & host & " failed: " & DescribeSocketFailure(ex))
            Finally
                socket.Dispose()
            End Try
        End Function

        ''' <summary>
        ''' One HTTP request over the URL's own scheme. The control URL is plain HTTP, so
        ''' it can only fail for transport reasons; the Proton URL is HTTPS, so a
        ''' difference between the two is the transport stack and not the internet. Only
        ''' the host is logged: a query string is where a token would be.
        ''' </summary>
        Private Shared Async Function ProbeHttpAsync(label As String, url As String) As Task
            Try
                Using client As New HttpClient()
                    Using cancellation As New CancellationTokenSource(TimeSpan.FromMilliseconds(DiagnoseProbeTimeoutMs))
                        Using request As New HttpRequestMessage(HttpMethod.Get, New Uri(url))
                            Using response As HttpResponseMessage = Await client.SendRequestAsync(request).AsTask(cancellation.Token)
                                Log.Info("netdiag: http " & label & " " & request.RequestUri.Host & " -> " &
                                         CInt(response.StatusCode).ToString())
                            End Using
                        End Using
                    End Using
                End Using
            Catch ex As Exception
                Log.Warn("netdiag: http " & label & " failed: " & DescribeSocketFailure(ex))
            End Try
        End Function

        ''' <summary>
        ''' The type and message plus, when the HResult is one, the socket status that
        ''' says what actually refused: HostNameNotResolved, NetworkDown and
        ''' ConnectionRefused are three different answers to the same question.
        ''' </summary>
        Private Shared Function DescribeSocketFailure(ex As Exception) As String
            Dim text As String = ex.GetType().Name & " " & ex.Message
            Try
                Dim status As SocketErrorStatus = SocketError.GetStatus(ex.HResult)
                If status <> SocketErrorStatus.Unknown Then
                    text = text & " (" & status.ToString() & ")"
                End If
            Catch
                ' Not a socket status: the type and message above are all there is.
            End Try
            Return text
        End Function

    End Class
End Namespace
