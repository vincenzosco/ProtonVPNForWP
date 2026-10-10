' TLS 1.3 client (RFC 8446) for the one connection shape this app needs: a single
' x25519 key share, TLS_AES_128_GCM_SHA256, rsa_pss_rsae_sha256, ALPN http/1.1.
'
' The server is authenticated before any application byte is trusted: the
' CertificateVerify signature over the handshake transcript is checked with the
' leaf key, the chain is verified against the pinned ISRG root, and the server
' Finished is checked against the derived key. Any disagreement aborts.
Imports System.Threading.Tasks

' Crypto.* and Services.* are referenced fully qualified, never through Imports:
' the shipping project's RootNamespace (Proton_VPN_WP) makes an `Imports Crypto`
' resolve nowhere, while the desktop harness compiles the same files without a
' root namespace. A qualified sibling name resolves in both.

Namespace Tls
    Friend NotInheritable Class TlsClient

        Private Const VersionTls13 As Integer = &H304
        Private Const SuiteAes128GcmSha256 As Integer = &H1301
        Private Const GroupX25519 As Integer = &H1D
        Private Const SignatureRsaPssRsaeSha256 As Integer = &H804
        Private Const AlertLevelWarning As Integer = 1
        Private Const AlertCloseNotify As Integer = 0

        Private ReadOnly _transport As ITlsTransport
        Private ReadOnly _random As Services.IRandomSource
        Private ReadOnly _host As String
        Private ReadOnly _transcript As New System.Collections.Generic.List(Of Byte)()

        Private _clientPrivate As Byte()
        Private _clientPublic As Byte()
        Private _readKey As TrafficKey
        Private _writeKey As TrafficKey

        Friend Property NegotiatedSuite As Integer

        ' Handshake diagnostics, set as the handshake progresses; the harness reads
        ' them to explain a failure that would otherwise be a bare tag mismatch.
        Friend ServerHelloVersion As Integer
        Friend ServerHelloSuite As Integer
        Friend ServerPublicLength As Integer
        Friend HandshakeTranscriptHex As String
        ''' <summary>
        ''' Public handshake facts (versions, suite, key length, transcript hash), set as
        ''' the handshake runs; the desktop harness prints them to explain a failure that
        ''' would otherwise be a bare "the server refused". Nothing secret goes in here:
        ''' no traffic secret, no ephemeral private key, no derived key.
        ''' </summary>
        Friend Shared LastDiagnostics As String = "(none)"

        ''' <summary>The cipher suite the last handshake settled on, for the desktop harness.</summary>
        Friend Shared LastNegotiatedSuite As Integer

        Private Sub New(transport As ITlsTransport, random As Services.IRandomSource, host As String)
            _transport = transport
            _random = random
            _host = host
        End Sub

        Friend Shared Async Function ConnectAsync(transport As ITlsTransport, random As Services.IRandomSource, host As String) As Task(Of TlsClient)
            Dim client As New TlsClient(transport, random, host)
            Await client.HandshakeAsync()
            Return client
        End Function

        Friend Async Function WriteAsync(data As Byte()) As Task
            Dim record As Byte() = TlsRecord.Protect(_writeKey, 23, data)
            Await _transport.WriteAsync(record, 0, record.Length)
        End Function

        ''' <summary>One application-data payload, skipping any post-handshake messages.</summary>
        Friend Async Function ReadAsync() As Task(Of Byte())
            While True
                Dim record As RecordData = Await TlsRecord.ReadAsync(_transport, _readKey)
                If record Is Nothing Then Return Nothing
                If record.ContentType = 23 Then Return record.Payload
                If record.ContentType = 21 Then
                    Dim alert As AlertInfo = AlertInfo.Parse(record.Payload)
                    ' close_notify is the peer closing the stream cleanly: end of data, not a failure.
                    If alert.Level = AlertLevelWarning AndAlso alert.Description = AlertCloseNotify Then Return Nothing
                    ' A warning the connection survives (user_canceled) is skipped like any other record.
                    If alert.Level = AlertLevelWarning Then Continue While
                    Throw New System.IO.IOException("The server sent a fatal TLS alert: " & alert.Describe() & ".")
                End If
                ' Handshake (NewSessionTicket) or other: skip.
            End While
        End Function

        Friend Sub Close()
            _transport.Close()
        End Sub

        Private Async Function HandshakeAsync() As Task
            _clientPrivate = _random.NextBytes(32)
            _clientPublic = Crypto.X25519.PublicFromPrivate(_clientPrivate)

            Dim clientHello As Byte() = BuildClientHello()
            Append(clientHello)
            ' The ClientHello must be framed in a plaintext handshake record; RFC 8446
            ' sets that record's legacy version to 0x0301.
            Dim clientHelloRecord As Byte() = Concat(
                New Byte() {22, &H3, &H1, CByte((clientHello.Length >> 8) And &HFF), CByte(clientHello.Length And &HFF)},
                clientHello)
            Await _transport.WriteAsync(clientHelloRecord, 0, clientHelloRecord.Length)

            Dim hello As RecordData = Await TlsRecord.ReadAsync(_transport, Nothing)
            If hello Is Nothing Then Throw New System.IO.IOException("The server closed the connection instead of answering.")
            If hello.ContentType <> 22 Then
                Dim detail As String = ""
                If hello.Payload IsNot Nothing Then
                    For i As Integer = 0 To System.Math.Min(7, hello.Payload.Length - 1)
                        detail &= hello.Payload(i).ToString("x2") & " "
                    Next
                End If
                Throw New System.IO.IOException("The server answered with TLS record type " & hello.ContentType.ToString() & " (" & detail & ").")
            End If
            Append(hello.Payload)
            Dim serverPublic As Byte() = ParseServerHello(hello.Payload)
            ServerPublicLength = serverPublic.Length

            Dim sharedSecret As Byte() = Crypto.X25519.SharedSecret(_clientPrivate, serverPublic)
            Dim clientHelloServerHelloHash As Byte() = TranscriptHash()
            HandshakeTranscriptHex = Crypto.CryptoBytes.ToHex(clientHelloServerHelloHash)
            LastDiagnostics = "server_version=" & ServerHelloVersion.ToString() & " server_suite=0x" & ServerHelloSuite.ToString("x4") & " server_key_len=" & serverPublic.Length.ToString() & " transcript=" & HandshakeTranscriptHex

            Dim early As Byte() = TlsKeys.EarlySecret()
            Dim handshakeSecret As Byte() = TlsKeys.HandshakeSecret(early, sharedSecret)
            Dim clientHandshakeSecret As Byte() = TlsKeys.ClientHandshakeTrafficSecret(handshakeSecret, clientHelloServerHelloHash)
            Dim serverHandshakeSecret As Byte() = TlsKeys.ServerHandshakeTrafficSecret(handshakeSecret, clientHelloServerHelloHash)

            _readKey = TlsRecord.NewKey(serverHandshakeSecret)
            _writeKey = TlsRecord.NewKey(clientHandshakeSecret)

            Dim chain As New System.Collections.Generic.List(Of Crypto.X509.Certificate)()
            Dim certificateVerifyAlgorithm As Integer = 0
            Dim certificateVerifySignature As Byte() = Nothing
            Dim certificateVerifyHash As Byte() = Nothing
            Dim serverFinished As Byte() = Nothing
            Dim finishedHash As Byte() = Nothing

            While serverFinished Is Nothing
                Dim record As RecordData = Await TlsRecord.ReadAsync(_transport, _readKey)
                If record Is Nothing Then Throw New System.IO.IOException("The connection closed during the handshake.")
                If record.ContentType <> 22 Then Throw New System.IO.IOException("Unexpected TLS record during the handshake.")

                Dim body As Byte() = record.Payload
                Dim offset As Integer = 0
                While offset < body.Length
                    Dim message As Byte() = ReadHandshakeMessage(body, offset)
                    Select Case message(0)
                        Case 11
                            ParseCertificate(message, chain)
                            Append(message)
                        Case 15
                            certificateVerifyHash = TranscriptHash()
                            ParseCertificateVerify(message, certificateVerifyAlgorithm, certificateVerifySignature)
                            Append(message)
                        Case 20
                            finishedHash = TranscriptHash()
                            serverFinished = FinishedData(message)
                            Append(message)
                        Case Else
                            Append(message)
                    End Select
                End While
            End While

            If chain.Count = 0 Then Throw New System.IO.IOException("The server did not send a certificate.")
            If certificateVerifySignature Is Nothing Then Throw New System.IO.IOException("The server did not prove possession of its key.")
            If certificateVerifyAlgorithm <> SignatureRsaPssRsaeSha256 Then Throw New System.IO.IOException("The server used an unexpected signature algorithm.")

            Dim signedContent As Byte() = CertificateVerifyContent(certificateVerifyHash)
            If Not Crypto.RsaVerify.VerifyPssSha256(chain(0).Modulus, chain(0).Exponent, signedContent, certificateVerifySignature) Then
                Throw New System.IO.IOException("The server's certificate proof did not verify.")
            End If

            Dim chainError As String = Crypto.X509.VerifyChain(chain, _host, System.DateTime.UtcNow)
            If chainError IsNot Nothing Then Throw New System.IO.IOException(chainError)

            Dim expectedFinished As Byte() = TlsKeys.FinishedVerifyData(TlsKeys.FinishedKey(serverHandshakeSecret), finishedHash)
            If Not Crypto.CryptoBytes.ConstantTimeEquals(expectedFinished, serverFinished) Then
                Throw New System.IO.IOException("The server's Finished did not verify.")
            End If

            ' The transcript through the server's Finished. The client's own Finished and
            ' both application secrets are derived from this, not from `finishedHash`,
            ' which stops one message earlier on purpose (server verify_data excludes it).
            Dim afterServerFinished As Byte() = TranscriptHash()

            Dim masterSecret As Byte() = TlsKeys.MasterSecret(handshakeSecret)
            Dim clientApplicationSecret As Byte() = TlsKeys.ClientApplicationTrafficSecret(masterSecret, afterServerFinished)
            Dim serverApplicationSecret As Byte() = TlsKeys.ServerApplicationTrafficSecret(masterSecret, afterServerFinished)

            Dim clientFinished As Byte() = HandshakeMessage(20, TlsKeys.FinishedVerifyData(TlsKeys.FinishedKey(clientHandshakeSecret), afterServerFinished))
            Dim recordBytes As Byte() = TlsRecord.Protect(_writeKey, 22, clientFinished)
            Await _transport.WriteAsync(recordBytes, 0, recordBytes.Length)

            _readKey = TlsRecord.NewKey(serverApplicationSecret)
            _writeKey = TlsRecord.NewKey(clientApplicationSecret)
            NegotiatedSuite = SuiteAes128GcmSha256
            LastNegotiatedSuite = SuiteAes128GcmSha256
        End Function

        Private Function BuildClientHello() As Byte()
            ' Every list-valued extension carries its own 2-byte (or 1-byte) list length
            ' inside the extension data; omitting one makes the server reject the hello.
            Dim extensions As New System.Collections.Generic.List(Of Byte)()
            Dim serverName As Byte() = Concat(New Byte() {0}, U16(_host.Length), Crypto.CryptoBytes.AsciiBytes(_host))
            AddRange(extensions, Extension(&H0, Concat(U16(serverName.Length), serverName)))
            AddRange(extensions, Extension(&HA, Concat(U16(2), U16(GroupX25519))))
            AddRange(extensions, Extension(&HD, Concat(U16(2), U16(SignatureRsaPssRsaeSha256))))
            Dim alpn As Byte() = Concat(New Byte() {CByte(8)}, Crypto.CryptoBytes.AsciiBytes("http/1.1"))
            AddRange(extensions, Extension(&H10, Concat(U16(alpn.Length), alpn)))
            AddRange(extensions, Extension(&H2B, Concat(New Byte() {CByte(2)}, U16(VersionTls13))))
            AddRange(extensions, Extension(&H33, Concat(U16(36), U16(GroupX25519), U16(32), _clientPublic)))

            Dim body As New System.Collections.Generic.List(Of Byte)()
            AddRange(body, U16(&H303))
            AddRange(body, _random.NextBytes(32))
            Dim sessionId As Byte() = _random.NextBytes(32)
            body.Add(CByte(32))
            AddRange(body, sessionId)
            AddRange(body, U16(2))
            AddRange(body, U16(SuiteAes128GcmSha256))
            body.Add(CByte(1))
            body.Add(CByte(0))
            AddRange(body, U16(extensions.Count))
            AddRange(body, extensions.ToArray())

            Return HandshakeMessage(1, body.ToArray())
        End Function

        Private Function ParseServerHello(message As Byte()) As Byte()
            Dim offset As Integer = 4
            ServerHelloVersion = (CInt(message(offset)) << 8) Or CInt(message(offset + 1))
            offset += 2
            offset += 32
            Dim sessionLength As Integer = CInt(message(offset))
            offset += 1 + sessionLength
            ServerHelloSuite = (CInt(message(offset)) << 8) Or CInt(message(offset + 1))
            offset += 2
            offset += 1
            Dim extensionsLength As Integer = (CInt(message(offset)) << 8) Or CInt(message(offset + 1))
            offset += 2
            Dim finish As Integer = offset + extensionsLength

            While offset + 4 <= finish
                Dim extensionType As Integer = (CInt(message(offset)) << 8) Or CInt(message(offset + 1))
                Dim extensionLength As Integer = (CInt(message(offset + 2)) << 8) Or CInt(message(offset + 3))
                Dim data As Integer = offset + 4
                If extensionType = 51 Then
                    Dim keyLength As Integer = (CInt(message(data + 2)) << 8) Or CInt(message(data + 3))
                    Dim key(keyLength - 1) As Byte
                    Array.Copy(message, data + 4, key, 0, keyLength)
                    Return key
                End If
                offset = data + extensionLength
            End While
            Throw New System.IO.IOException("The server did not send an x25519 key share.")
        End Function

        Private Shared Sub ParseCertificate(message As Byte(), chain As System.Collections.Generic.List(Of Crypto.X509.Certificate))
            Dim offset As Integer = 4
            Dim contextLength As Integer = CInt(message(offset))
            offset += 1 + contextLength
            Dim listLength As Integer = (CInt(message(offset)) << 16) Or (CInt(message(offset + 1)) << 8) Or CInt(message(offset + 2))
            offset += 3
            Dim finish As Integer = offset + listLength

            While offset + 3 <= finish
                Dim certificateLength As Integer = (CInt(message(offset)) << 16) Or (CInt(message(offset + 1)) << 8) Or CInt(message(offset + 2))
                offset += 3
                Dim der(certificateLength - 1) As Byte
                Array.Copy(message, offset, der, 0, certificateLength)
                chain.Add(Crypto.X509.Parse(der))
                offset += certificateLength
                If offset + 2 <= finish Then
                    Dim extensionLength As Integer = (CInt(message(offset)) << 8) Or CInt(message(offset + 1))
                    offset += 2 + extensionLength
                End If
            End While
        End Sub

        Private Shared Sub ParseCertificateVerify(message As Byte(), ByRef algorithm As Integer, ByRef signature As Byte())
            Dim offset As Integer = 4
            algorithm = (CInt(message(offset)) << 8) Or CInt(message(offset + 1))
            offset += 2
            Dim length As Integer = (CInt(message(offset)) << 8) Or CInt(message(offset + 1))
            offset += 2
            signature = New Byte(length - 1) {}
            Array.Copy(message, offset, signature, 0, length)
        End Sub

        Private Shared Function FinishedData(message As Byte()) As Byte()
            Dim length As Integer = message.Length - 4
            Dim result(length - 1) As Byte
            Array.Copy(message, 4, result, 0, length)
            Return result
        End Function

        Private Shared Function CertificateVerifyContent(transcriptHash As Byte()) As Byte()
            Dim spaces(63) As Byte
            For i As Integer = 0 To 63
                spaces(i) = &H20
            Next
            Return Concat(spaces, Crypto.CryptoBytes.AsciiBytes("TLS 1.3, server CertificateVerify"), New Byte() {0}, transcriptHash)
        End Function

        Private Shared Function ReadHandshakeMessage(body As Byte(), ByRef offset As Integer) As Byte()
            If offset + 4 > body.Length Then Throw New System.IO.IOException("Truncated handshake message.")
            Dim length As Integer = (CInt(body(offset + 1)) << 16) Or (CInt(body(offset + 2)) << 8) Or CInt(body(offset + 3))
            Dim total As Integer = 4 + length
            If offset + total > body.Length Then Throw New System.IO.IOException("Truncated handshake message.")
            Dim message(total - 1) As Byte
            Array.Copy(body, offset, message, 0, total)
            offset += total
            Return message
        End Function

        Private Sub Append(data As Byte())
            If data IsNot Nothing Then _transcript.AddRange(data)
        End Sub

        Private Function TranscriptHash() As Byte()
            Return Crypto.Sha256.Hash(_transcript.ToArray())
        End Function

        Private Shared Function HandshakeMessage(messageType As Byte, body As Byte()) As Byte()
            Return Concat(New Byte() {messageType, CByte((body.Length >> 16) And &HFF), CByte((body.Length >> 8) And &HFF), CByte(body.Length And &HFF)}, body)
        End Function

        Private Shared Function Extension(extensionType As Integer, data As Byte()) As Byte()
            Return Concat(U16(extensionType), U16(data.Length), data)
        End Function

        Private Shared Function U16(value As Integer) As Byte()
            Return New Byte() {CByte((value >> 8) And &HFF), CByte(value And &HFF)}
        End Function

        Private Shared Function Concat(ParamArray parts As Byte()()) As Byte()
            Return Crypto.CryptoBytes.Concat(parts)
        End Function

        Private Shared Sub AddRange(target As System.Collections.Generic.List(Of Byte), data As Byte())
            If data IsNot Nothing Then target.AddRange(data)
        End Sub

    End Class

    ''' <summary>
    ''' A TLS alert (RFC 8446 section 6). Level 1 is a warning the connection survives;
    ''' close_notify is the peer's clean end of stream, and everything else is fatal.
    ''' </summary>
    Friend NotInheritable Class AlertInfo

        Friend Level As Integer
        Friend Description As Integer

        Private Sub New()
        End Sub

        Friend Shared Function Parse(payload As Byte()) As AlertInfo
            Dim alert As New AlertInfo()
            If payload IsNot Nothing AndAlso payload.Length >= 2 Then
                alert.Level = CInt(payload(0))
                alert.Description = CInt(payload(1))
            Else
                ' A truncated alert cannot be classified; treat it as fatal.
                alert.Level = 2
            End If
            Return alert
        End Function

        Friend Function Describe() As String
            Return Description.ToString() & " " & Name() & " (level " & Level.ToString() & ")"
        End Function

        Private Function Name() As String
            Select Case Description
                Case 0
                    Return "close_notify"
                Case 10
                    Return "unexpected_message"
                Case 20
                    Return "bad_record_mac"
                Case 22
                    Return "record_overflow"
                Case 40
                    Return "handshake_failure"
                Case 42
                    Return "bad_certificate"
                Case 43
                    Return "unsupported_certificate"
                Case 47
                    Return "illegal_parameter"
                Case 48
                    Return "unknown_ca"
                Case 49
                    Return "access_denied"
                Case 70
                    Return "protocol_version"
                Case 80
                    Return "internal_error"
                Case 90
                    Return "user_canceled"
                Case 109
                    Return "missing_extension"
                Case 110
                    Return "unsupported_extension"
                Case 112
                    Return "unrecognized_name"
                Case 116
                    Return "certificate_required"
                Case 120
                    Return "no_application_protocol"
                Case Else
                    Return "unknown"
            End Select
        End Function

    End Class
End Namespace
