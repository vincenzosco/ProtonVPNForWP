' A minimal HTTP/1.1 client over the TLS 1.3 stack.
'
' The app only ever POSTs or GETs Proton's JSON APIs, so this covers exactly that:
' request line, a fixed header set, and a response whose body is read to the end
' (Connection: close) and decoded from either Content-Length or chunked. A
' redirect to another host is refused rather than followed.
Imports System.Collections.Generic
Imports System.Threading.Tasks

' Services.* is referenced fully qualified: the shipping project's RootNamespace
' makes an `Imports Services` resolve nowhere while the desktop harness compiles
' these files without one. A qualified sibling name resolves in both.

Namespace Tls
    Friend NotInheritable Class HttpReply
        Friend StatusCode As Integer
        Friend Body As String
    End Class

    Friend NotInheritable Class TlsHttpClient

        Private Sub New()
        End Sub

        Friend Shared Async Function PostAsync(transport As ITlsTransport, random As Services.IRandomSource,
                                               url As String, jsonBody As String,
                                               headers As IDictionary(Of String, String)) As Task(Of HttpReply)
            Dim body As Byte() = If(jsonBody Is Nothing, New Byte() {}, Crypto.CryptoBytes.AsciiBytes(jsonBody))
            Dim extra As New Dictionary(Of String, String)()
            extra("Content-Type") = "application/json"
            If headers IsNot Nothing Then
                For Each pair As KeyValuePair(Of String, String) In headers
                    extra(pair.Key) = pair.Value
                Next
            End If
            Return Await SendAsync(transport, random, "POST", url, body, extra)
        End Function

        Friend Shared Async Function GetAsync(transport As ITlsTransport, random As Services.IRandomSource,
                                              url As String,
                                              headers As IDictionary(Of String, String)) As Task(Of HttpReply)
            Return Await SendAsync(transport, random, "GET", url, New Byte() {}, headers)
        End Function

        Private Shared Async Function SendAsync(transport As ITlsTransport, random As Services.IRandomSource,
                                                method As String, url As String, body As Byte(),
                                                headers As IDictionary(Of String, String)) As Task(Of HttpReply)
            Dim uri As New System.Uri(url)
            If uri.Scheme <> "https" Then Throw New System.ArgumentException("Only https URLs are supported.")

            Dim client As TlsClient = Await TlsClient.ConnectAsync(transport, random, uri.Host)
            Try
                Dim request As String = method & " " & PathOf(uri) & " HTTP/1.1" & vbCrLf &
                                        "Host: " & uri.Host & vbCrLf &
                                        "Connection: close" & vbCrLf &
                                        "Content-Length: " & body.Length.ToString() & vbCrLf
                If headers IsNot Nothing Then
                    For Each pair As KeyValuePair(Of String, String) In headers
                        request &= pair.Key & ": " & pair.Value & vbCrLf
                    Next
                End If
                request &= vbCrLf

                Dim head As Byte() = Crypto.CryptoBytes.AsciiBytes(request)
                Dim payload As Byte() = Crypto.CryptoBytes.Concat(head, body)
                Await client.WriteAsync(payload)

                Dim received As New List(Of Byte)()
                While True
                    Dim chunk As Byte() = Await client.ReadAsync()
                    If chunk Is Nothing Then Exit While
                    received.AddRange(chunk)
                End While

                Dim all As Byte() = received.ToArray()
                Dim headerEnd As Integer = IndexOfSequence(all, New Byte() {13, 10, 13, 10}, 0)
                If headerEnd < 0 Then Throw New System.IO.IOException("The server sent no HTTP header terminator.")

                Dim headerText As String = AsciiOf(all, 0, headerEnd)
                Dim status As Integer = ParseStatus(headerText)
                Dim bodyStart As Integer = headerEnd + 4
                Dim bodyLength As Integer = all.Length - bodyStart
                Dim bodyBytes As Byte()
                If bodyLength > 0 Then
                    bodyBytes = New Byte(bodyLength - 1) {}
                    Array.Copy(all, bodyStart, bodyBytes, 0, bodyLength)
                Else
                    bodyBytes = New Byte() {}
                End If
                If headerText.ToLowerInvariant().Contains("transfer-encoding: chunked") Then
                    bodyBytes = Dechunk(bodyBytes)
                End If

                Dim reply As New HttpReply()
                reply.StatusCode = status
                reply.Body = AsciiOf(bodyBytes, 0, bodyBytes.Length)
                Return reply
            Finally
                client.Close()
            End Try
        End Function

        Private Shared Function PathOf(uri As System.Uri) As String
            Dim path As String = uri.AbsolutePath
            If String.IsNullOrEmpty(path) Then path = "/"
            If Not String.IsNullOrEmpty(uri.Query) Then path &= uri.Query
            Return path
        End Function

        Private Shared Function ParseStatus(headerText As String) As Integer
            Dim firstLineEnd As Integer = headerText.IndexOf(vbCrLf)
            Dim firstLine As String = If(firstLineEnd < 0, headerText, headerText.Substring(0, firstLineEnd))
            Dim parts As String() = firstLine.Split(" "c)
            If parts.Length < 2 Then Return 0
            Dim code As Integer
            If Integer.TryParse(parts(1), code) Then Return code
            Return 0
        End Function

        Private Shared Function Dechunk(data As Byte()) As Byte()
            Dim result As New List(Of Byte)()
            Dim offset As Integer = 0
            While offset < data.Length
                Dim lineEnd As Integer = IndexOfSequence(data, New Byte() {13, 10}, offset)
                If lineEnd < 0 Then Exit While
                Dim sizeText As String = AsciiOf(data, offset, lineEnd - offset).Trim()
                Dim semi As Integer = sizeText.IndexOf(";"c)
                If semi >= 0 Then sizeText = sizeText.Substring(0, semi)
                Dim size As Integer
                If Not Integer.TryParse(sizeText, System.Globalization.NumberStyles.HexNumber,
                                        System.Globalization.CultureInfo.InvariantCulture, size) Then Exit While
                offset = lineEnd + 2
                If size = 0 Then Exit While
                If offset + size > data.Length Then Exit While
                For i As Integer = 0 To size - 1
                    result.Add(data(offset + i))
                Next
                offset += size + 2
            End While
            Return result.ToArray()
        End Function

        Private Shared Function IndexOfSequence(haystack As Byte(), needle As Byte(), start As Integer) As Integer
            For i As Integer = start To haystack.Length - needle.Length
                Dim matched As Boolean = True
                For j As Integer = 0 To needle.Length - 1
                    If haystack(i + j) <> needle(j) Then
                        matched = False
                        Exit For
                    End If
                Next
                If matched Then Return i
            Next
            Return -1
        End Function

        Private Shared Function AsciiOf(data As Byte(), offset As Integer, count As Integer) As String
            If data Is Nothing OrElse count <= 0 Then Return String.Empty
            Dim chars(count - 1) As Char
            For i As Integer = 0 To count - 1
                chars(i) = ChrW(CInt(data(offset + i)))
            Next
            Return New String(chars)
        End Function

    End Class
End Namespace
