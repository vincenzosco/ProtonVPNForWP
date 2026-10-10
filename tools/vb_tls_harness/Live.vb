' Live driver for the SHIPPING TLS 1.3 client.
'
' Compiled together with the app's own Services\*.vb files, so a green run proves
' the code that ships completes a real handshake and HTTP exchange with Proton --
' something the phone cannot do through its own TLS stack at all.
'
' Usage:
'   Live.exe <host>
' Prints `status=<code>` and the first bytes of the body.
Imports System
Imports System.Net.Sockets
Imports System.Threading.Tasks
Imports Services
Imports Tls

Module Program

    Sub Main(args As String())
        If args.Length < 1 Then
            Console.Error.WriteLine("usage: Live <host>")
            Environment.Exit(2)
        End If
        Try
            Run(args(0))
            Environment.Exit(0)
        Catch ex As Exception
            Console.Error.WriteLine("LIVE FAILURE: " & ex.GetType().Name & ": " & ex.Message)
            Console.Error.WriteLine("DIAG: " & TlsClient.LastDiagnostics)
            Environment.Exit(1)
        End Try
    End Sub

    Private Sub Run(host As String)
        Dim url As String = "https://" & host & "/api/auth/info"
        Dim transport As New SocketTransport(host, 443)
        Dim headers As New System.Collections.Generic.Dictionary(Of String, String)()
        headers("Accept") = "application/vnd.protonmail.v1+json"
        ' The identifier Proton's API accepts for a VPN client. "web-vpn@5.0.0", the
        ' value the app shipped with, is answered with 400 Code 5002 ("Invalid app
        ' version") before any authentication happens.
        headers("x-pm-appversion") = "windows-vpn@5.0.0.0"

        Dim reply As HttpReply = TlsHttpClient.PostAsync(transport, New DesktopRandom(), url,
            "{""Username"":""probe@example.invalid""}", headers).GetAwaiter().GetResult()

        Console.WriteLine("suite=0x" & TlsClient.LastNegotiatedSuite.ToString("x4"))
        Console.WriteLine("status=" & reply.StatusCode.ToString())
        Dim body As String = If(reply.Body, String.Empty)
        body = body.Replace(vbCr, " ").Replace(vbLf, " ")
        If body.Length > 200 Then body = body.Substring(0, 200)
        Console.WriteLine("body=" & body)
    End Sub

End Module

Friend NotInheritable Class SocketTransport
    Implements ITlsTransport

    Private ReadOnly _client As TcpClient
    Private ReadOnly _stream As NetworkStream

    Friend Sub New(host As String, port As Integer)
        _client = New TcpClient()
        _client.Connect(host, port)
        _stream = _client.GetStream()
    End Sub

    Friend Function ReadAsync(buffer As Byte(), offset As Integer, count As Integer) As Task(Of Integer) Implements ITlsTransport.ReadAsync
        Return _stream.ReadAsync(buffer, offset, count)
    End Function

    Friend Function WriteAsync(buffer As Byte(), offset As Integer, count As Integer) As Task Implements ITlsTransport.WriteAsync
        Return _stream.WriteAsync(buffer, offset, count)
    End Function

    Friend Sub CloseTransport() Implements ITlsTransport.Close
        Try
            _client.Close()
        Catch
        End Try
    End Sub

End Class

Friend NotInheritable Class DesktopRandom
    Implements IRandomSource

    Friend Function NextBytes(count As Integer) As Byte() Implements IRandomSource.NextBytes
        Dim buffer(count - 1) As Byte
        Using rng As New System.Security.Cryptography.RNGCryptoServiceProvider()
            rng.GetBytes(buffer)
        End Using
        Return buffer
    End Function

End Class
