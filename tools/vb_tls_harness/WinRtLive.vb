' Live driver for the app's OWN WinRT transport.
'
' The other live driver (Live.vb) uses a System.Net.Sockets transport, which is
' the harness's own code. This one compiles Services\SocketTlsTransport.vb -- the
' shipping implementation, the one the phone actually runs -- and drives it from
' the desktop, where the same WinRT surface (Windows.Networking.Sockets,
' Windows.Storage.Streams) exists.
'
' That leaves exactly one seam unexercised by every harness combined: nothing
' here proves the WP8.1 package loads, which is what a deploy to the emulator or
' the phone proves and nothing else does.
'
' Usage:
'   WinRtLive.exe <host>
' Prints `suite=`, `status=` and the first bytes of the body, like Live.vb.
Imports System
Imports System.Threading.Tasks
Imports Tls

Module Program

    Sub Main(args As String())
        If args.Length < 1 Then
            Console.Error.WriteLine("usage: WinRtLive <host>")
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
        Dim headers As New System.Collections.Generic.Dictionary(Of String, String)()
        headers("Accept") = "application/vnd.protonmail.v1+json"
        headers("x-pm-appversion") = "windows-vpn@5.0.0.0"

        ' The app's transport, connected exactly as the app connects it.
        Dim transport As SocketTlsTransport = SocketTlsTransport.ConnectAsync(host, 443).GetAwaiter().GetResult()
        Dim reply As HttpReply = TlsHttpClient.PostAsync(transport, New WinRtRandom(), url,
            "{""Username"":""probe@example.invalid""}", headers).GetAwaiter().GetResult()

        Console.WriteLine("suite=0x" & TlsClient.LastNegotiatedSuite.ToString("x4"))
        Console.WriteLine("status=" & reply.StatusCode.ToString())
        Dim body As String = If(reply.Body, String.Empty)
        body = body.Replace(vbCr, " ").Replace(vbLf, " ")
        If body.Length > 200 Then body = body.Substring(0, 200)
        Console.WriteLine("body=" & body)
    End Sub

End Module

''' <summary>
''' Desktop stand-in for Services.SystemRandomSource (WinRT CryptographicBuffer),
''' which lives in RandomSource.vb next to the SRP secret generator and would drag
''' the whole bcrypt/SRP tree into this compile. The seam is the same interface.
''' </summary>
Friend NotInheritable Class WinRtRandom
    Implements Services.IRandomSource

    Friend Function NextBytes(count As Integer) As Byte() Implements Services.IRandomSource.NextBytes
        Dim buffer(count - 1) As Byte
        Using rng As New System.Security.Cryptography.RNGCryptoServiceProvider()
            rng.GetBytes(buffer)
        End Using
        Return buffer
    End Function

End Class
