' Pure managed HKDF-SHA256 (RFC 5869), plus the TLS 1.3 HKDF-Expand-Label
' wrapper (RFC 8446 section 7.1).
'
' The TLS 1.3 key schedule is nothing but Extract and Expand with protocol
' labels, so both live here next to the primitive they are built from.
Namespace Crypto
    Friend NotInheritable Class Hkdf

        Private Const HashLen As Integer = 32

        Private Sub New()
        End Sub

        ''' <summary>HKDF-Extract: PRK = HMAC(salt, IKM). An empty salt is HashLen zeros.</summary>
        Friend Shared Function Extract(salt As Byte(), ikm As Byte()) As Byte()
            If salt Is Nothing OrElse salt.Length = 0 Then salt = New Byte(HashLen - 1) {}
            If ikm Is Nothing Then ikm = New Byte() {}
            Return Hmac.Sha256(salt, ikm)
        End Function

        ''' <summary>HKDF-Expand: T(1) || T(2) ... truncated to <paramref name="length"/>.</summary>
        Friend Shared Function Expand(prk As Byte(), info As Byte(), length As Integer) As Byte()
            If info Is Nothing Then info = New Byte() {}
            Dim okm(length - 1) As Byte
            Dim t As Byte() = New Byte() {}
            Dim offset As Integer = 0
            Dim counter As Integer = 1
            While offset < length
                Dim block As Byte() = Hmac.Sha256(prk, CryptoBytes.Concat(t, info, New Byte() {CByte(counter)}))
                Dim take As Integer = System.Math.Min(block.Length, length - offset)
                Array.Copy(block, 0, okm, offset, take)
                offset += take
                t = block
                counter += 1
            End While
            Return okm
        End Function

        ''' <summary>
        ''' HKDF-Expand-Label: the label is prefixed with "tls13 " and both the length
        ''' and the context are length-prefixed, exactly as RFC 8446 fixes it.
        ''' </summary>
        Friend Shared Function ExpandLabel(secret As Byte(), label As String, context As Byte(), length As Integer) As Byte()
            If context Is Nothing Then context = New Byte() {}
            Dim fullLabel As Byte() = CryptoBytes.AsciiBytes("tls13 " & label)
            Dim info As Byte() = CryptoBytes.Concat(
                New Byte() {CByte((length >> 8) And &HFF), CByte(length And &HFF)},
                New Byte() {CByte(fullLabel.Length)},
                fullLabel,
                New Byte() {CByte(context.Length)},
                context)
            Return Expand(secret, info, length)
        End Function

    End Class
End Namespace
