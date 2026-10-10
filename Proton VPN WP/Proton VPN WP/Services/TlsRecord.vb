' TLS 1.3 record layer (RFC 8446 section 5.2).
'
' Every protected record is AEAD over the 5-byte record header as associated data,
' with the nonce built from the traffic IV and the record sequence number. The
' plaintext is the payload followed by the real content type; the outer type is
' always application_data (23). A record that fails authentication is an error,
' never a record that is skipped.
Imports System.Threading.Tasks

Namespace Tls
    Friend NotInheritable Class TrafficKey
        Friend Key As Byte()
        Friend Iv As Byte()
        Friend Sequence As ULong
    End Class

    Friend NotInheritable Class RecordData
        Friend ContentType As Byte
        Friend Payload As Byte()
    End Class

    Friend NotInheritable Class TlsRecord

        Private Const HeaderBytes As Integer = 5
        Private Const TagBytes As Integer = 16
        Private Const ContentHandshake As Byte = 22
        Private Const ContentApplicationData As Byte = 23
        Private Const ContentChangeCipherSpec As Byte = 20

        Private Sub New()
        End Sub

        Friend Shared Function NewKey(secret As Byte()) As TrafficKey
            Dim key As New TrafficKey()
            key.Key = TlsKeys.TrafficKey(secret)
            key.Iv = TlsKeys.TrafficIv(secret)
            key.Sequence = 0UL
            Return key
        End Function

        ''' <summary>Encrypts one record with the given traffic key, advancing its sequence.</summary>
        Friend Shared Function Protect(key As TrafficKey, contentType As Byte, payload As Byte()) As Byte()
            If payload Is Nothing Then payload = New Byte() {}
            Dim inner As Byte() = Crypto.CryptoBytes.Concat(payload, New Byte() {contentType})
            Dim header As Byte() = HeaderWithLength(inner.Length + TagBytes)
            Dim sealedBytes As Byte() = Crypto.Gcm.Seal(key.Key, Nonce(key.Iv, key.Sequence), header, inner)
            key.Sequence += 1UL
            Return Crypto.CryptoBytes.Concat(header, sealedBytes)
        End Function

        ''' <summary>
        ''' Reads one record. With <paramref name="key"/> Nothing the record is plaintext
        ''' (the handshake records through ServerHello); otherwise it is authenticated and
        ''' decrypted. ChangeCipherSpec records are skipped. Nothing means the stream ended.
        ''' </summary>
        Friend Shared Async Function ReadAsync(transport As ITlsTransport, key As TrafficKey) As Task(Of RecordData)
            Dim header(HeaderBytes - 1) As Byte
            If Not Await ReadExactAsync(transport, header, HeaderBytes) Then Return Nothing

            Dim length As Integer = (CInt(header(3)) << 8) Or CInt(header(4))
            Dim body(length - 1) As Byte
            If Not Await ReadExactAsync(transport, body, length) Then Return Nothing

            If header(0) = ContentChangeCipherSpec Then
                Return Await ReadAsync(transport, key)
            End If

            Dim record As New RecordData()
            If key Is Nothing Then
                record.ContentType = header(0)
                record.Payload = body
                Return record
            End If

            Dim inner As Byte() = Crypto.Gcm.Open(key.Key, Nonce(key.Iv, key.Sequence), header, body)
            key.Sequence += 1UL
            If inner Is Nothing Then Throw New System.IO.IOException("A TLS record failed authentication.")

            Dim last As Integer = inner.Length - 1
            While last >= 0 AndAlso inner(last) = 0
                last -= 1
            End While
            If last < 0 Then Throw New System.IO.IOException("A TLS record carried no content type.")

            record.ContentType = inner(last)
            Dim payload(last - 1) As Byte
            If last > 0 Then Array.Copy(inner, 0, payload, 0, last)
            record.Payload = payload
            Return record
        End Function

        Friend Shared Async Function ReadExactAsync(transport As ITlsTransport, buffer As Byte(), count As Integer) As Task(Of Boolean)
            Dim offset As Integer = 0
            While offset < count
                Dim read As Integer = Await transport.ReadAsync(buffer, offset, count - offset)
                If read <= 0 Then Return False
                offset += read
            End While
            Return True
        End Function

        Private Shared Function HeaderWithLength(length As Integer) As Byte()
            ' type application_data, legacy version 0x0303, 16-bit length.
            Return New Byte() {ContentApplicationData, &H3, &H3, CByte((length >> 8) And &HFF), CByte(length And &HFF)}
        End Function

        ' The local is not named `nonce`: a member with that name would shadow the
        ' method inside itself and turn `nonce(i)` into a call.
        Private Shared Function Nonce(iv As Byte(), sequence As ULong) As Byte()
            Dim result(11) As Byte
            Array.Copy(iv, result, 12)
            For i As Integer = 0 To 7
                result(11 - i) = CByte(CInt(result(11 - i)) Xor CInt((sequence >> (8 * i)) And &HFFUL))
            Next
            Return result
        End Function

    End Class
End Namespace
