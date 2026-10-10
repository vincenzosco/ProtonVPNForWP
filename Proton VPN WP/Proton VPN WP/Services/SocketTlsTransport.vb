' The app's side of the TLS transport seam: a plain TCP connection to host:443.
'
' Deliberately no SocketProtectionLevel. Connecting with one would hand the socket
' to Schannel, which is exactly the stack this app cannot use against Proton: the
' platform offers no cipher suite Proton's edge accepts, so the connect would fail
' before TlsClient ever saw a byte. The socket stays raw and TlsClient does the
' handshake.
'
' Kept in the app and out of the desktop harness for the same reason IRandomSource
' is: StreamSocket is WinRT-only, and the shipping type would not compile against
' vbc on the desktop. The harness supplies a System.Net.Sockets transport instead
' and exercises the same TlsClient.
Imports System.Runtime.InteropServices.WindowsRuntime
Imports System.Threading.Tasks
Imports Windows.Networking
Imports Windows.Networking.Sockets
Imports Windows.Storage.Streams

Namespace Tls
    Friend NotInheritable Class SocketTlsTransport
        Implements ITlsTransport

        Private Const ConnectTimeoutMs As Integer = 15000

        Private ReadOnly _socket As StreamSocket

        Private Sub New(socket As StreamSocket)
            _socket = socket
        End Sub

        ''' <summary>
        ''' Connects the raw socket. The connect is bounded: a black-holed address
        ''' otherwise leaves the sign-in button spinning with nothing to report.
        ''' </summary>
        Friend Shared Async Function ConnectAsync(host As String, port As Integer) As Task(Of SocketTlsTransport)
            Dim socket As New StreamSocket()
            Try
                Dim connectTask As Task = socket.ConnectAsync(New HostName(host), port.ToString()).AsTask()
                Dim finished As Task = Await Task.WhenAny(connectTask, Task.Delay(ConnectTimeoutMs))
                If Not Object.ReferenceEquals(finished, connectTask) Then
                    Throw New System.TimeoutException("Connecting to " & host & ":" & port.ToString() & " took longer than " &
                                                       ConnectTimeoutMs.ToString() & " ms.")
                End If
                ' Surfaces the failure the socket actually reported.
                Await connectTask
            Catch
                ' Disposing aborts a connect that is still in flight.
                socket.Dispose()
                Throw
            End Try
            Return New SocketTlsTransport(socket)
        End Function

        ''' <summary>
        ''' Reads up to <paramref name="count"/> bytes, returning as soon as any are
        ''' available (InputStreamOptions.Partial). TLS records do not align with read
        ''' boundaries, so the reader must never wait for a full buffer.
        ''' </summary>
        Friend Async Function ReadAsync(buffer As Byte(), offset As Integer, count As Integer) As Task(Of Integer) Implements ITlsTransport.ReadAsync
            ' ReadAsync fills the buffer it is handed and reports how much of it it
            ' filled; the buffer wraps the caller's array at the caller's offset, so
            ' the bytes land where TlsRecord expects them. The result is not named
            ' `IBuffer`: with both the Windows metadata and the api contract referenced,
            ' that name is ambiguous, and nothing here needs it.
            Dim filled = Await _socket.InputStream.ReadAsync(buffer.AsBuffer(offset, count), CUInt(count), InputStreamOptions.Partial).AsTask()
            Return CInt(filled.Length)
        End Function

        ''' <summary>Writes exactly the range given, without an intermediate copy.</summary>
        Friend Async Function WriteAsync(buffer As Byte(), offset As Integer, count As Integer) As Task Implements ITlsTransport.WriteAsync
            Await _socket.OutputStream.WriteAsync(buffer.AsBuffer(offset, count)).AsTask()
        End Function

        Friend Sub Close() Implements ITlsTransport.Close
            Try
                _socket.Dispose()
            Catch
                ' Closing an already-failed socket is not a second failure.
            End Try
        End Sub

    End Class
End Namespace
