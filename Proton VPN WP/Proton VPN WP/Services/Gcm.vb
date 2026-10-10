' Pure managed AES-128-GCM (NIST SP 800-38D).
'
' TLS 1.3 uses AEAD with a 12-byte nonce and a 16-byte tag; this implements exactly
' that shape. `Open` recomputes the tag and compares it in constant time before it
' returns any plaintext, so a forged or truncated record never yields data.
'
' Correctness is pinned by the GCM Test Case 2 vector in the desktop harness.
Namespace Crypto
    Friend NotInheritable Class Gcm

        Private Const TagBytes As Integer = 16

        Private Sub New()
        End Sub

        ''' <summary>Returns ciphertext || 16-byte tag.</summary>
        Friend Shared Function Seal(key As Byte(), nonce As Byte(), aad As Byte(), plaintext As Byte()) As Byte()
            If plaintext Is Nothing Then plaintext = New Byte() {}
            Dim h As Byte() = Aes.EncryptBlock(key, New Byte(15) {})
            Dim j0 As Byte() = InitialCounter(nonce)
            Dim ciphertext As Byte() = Gctr(key, Inc32(j0), plaintext)
            Dim s As Byte() = GHash(h, aad, ciphertext)
            Dim tag As Byte() = Gctr(key, j0, s)
            Return CryptoBytes.Concat(ciphertext, tag)
        End Function

        ''' <summary>Returns the plaintext, or Nothing when the tag does not match.</summary>
        Friend Shared Function Open(key As Byte(), nonce As Byte(), aad As Byte(), sealed As Byte()) As Byte()
            If sealed Is Nothing OrElse sealed.Length < TagBytes Then Return Nothing

            Dim ciphertext(sealed.Length - TagBytes - 1) As Byte
            Array.Copy(sealed, ciphertext, ciphertext.Length)
            Dim tag(15) As Byte
            Array.Copy(sealed, ciphertext.Length, tag, 0, TagBytes)

            Dim h As Byte() = Aes.EncryptBlock(key, New Byte(15) {})
            Dim j0 As Byte() = InitialCounter(nonce)
            Dim s As Byte() = GHash(h, aad, ciphertext)
            Dim expected As Byte() = Gctr(key, j0, s)
            If Not CryptoBytes.ConstantTimeEquals(expected, tag) Then Return Nothing
            Return Gctr(key, Inc32(j0), ciphertext)
        End Function

        ''' <summary>J0 for a 96-bit nonce: nonce || 0x00000001.</summary>
        Private Shared Function InitialCounter(nonce As Byte()) As Byte()
            If nonce Is Nothing OrElse nonce.Length <> 12 Then
                Throw New System.ArgumentException("GCM here supports only 96-bit nonces.")
            End If
            Return CryptoBytes.Concat(nonce, New Byte() {0, 0, 0, 1})
        End Function

        ''' <summary>Increments the low 32 bits of the counter block, big-endian.</summary>
        Private Shared Function Inc32(block As Byte()) As Byte()
            Dim r As Byte() = CType(block.Clone(), Byte())
            For i As Integer = 15 To 12 Step -1
                r(i) = CByte(r(i) + 1)
                If r(i) <> 0 Then Exit For
            Next
            Return r
        End Function

        ''' <summary>GCTR: XOR the input with the keystream from the counter blocks.</summary>
        Private Shared Function Gctr(key As Byte(), icb As Byte(), x As Byte()) As Byte()
            If x Is Nothing OrElse x.Length = 0 Then Return New Byte() {}
            Dim out(x.Length - 1) As Byte
            Dim counter As Byte() = CType(icb.Clone(), Byte())
            Dim offset As Integer = 0
            While offset < x.Length
                Dim stream As Byte() = Aes.EncryptBlock(key, counter)
                Dim blockLength As Integer = System.Math.Min(16, x.Length - offset)
                For i As Integer = 0 To blockLength - 1
                    out(offset + i) = CByte(x(offset + i) Xor stream(i))
                Next
                offset += blockLength
                counter = Inc32(counter)
            End While
            Return out
        End Function

        ''' <summary>GHASH over A || pad || C || pad || len(A) || len(C), all in bits.</summary>
        Private Shared Function GHash(h As Byte(), aad As Byte(), ciphertext As Byte()) As Byte()
            Dim y(15) As Byte
            FeedBlocks(y, h, aad)
            FeedBlocks(y, h, ciphertext)

            Dim lengths(15) As Byte
            WriteU64(lengths, 0, If(aad Is Nothing, 0L, CLng(aad.Length)))
            WriteU64(lengths, 8, If(ciphertext Is Nothing, 0L, CLng(ciphertext.Length)))
            Multiply(y, lengths, h)
            Return y
        End Function

        Private Shared Sub FeedBlocks(y As Byte(), h As Byte(), data As Byte())
            If data Is Nothing Then Return
            Dim offset As Integer = 0
            While offset < data.Length
                Dim block(15) As Byte
                Dim n As Integer = System.Math.Min(16, data.Length - offset)
                Array.Copy(data, offset, block, 0, n)
                Multiply(y, block, h)
                offset += n
            End While
        End Sub

        ''' <summary>y = (y xor block) * h in GF(2^128).</summary>
        Private Shared Sub Multiply(y As Byte(), block As Byte(), h As Byte())
            Dim x(15) As Byte
            For i As Integer = 0 To 15
                x(i) = CByte(y(i) Xor block(i))
            Next
            GfMultiply(x, h, y)
        End Sub

        ''' <summary>
        ''' The GCM field multiplication (SP 800-38D 6.3): MSB-first bits, right shifts,
        ''' and the reduction polynomial x^128 + x^7 + x^2 + x + 1.
        ''' </summary>
        Private Shared Sub GfMultiply(x As Byte(), y As Byte(), result As Byte())
            Dim z(15) As Byte
            Dim v(15) As Byte
            Array.Copy(y, v, 16)
            For i As Integer = 0 To 127
                Dim bit As Integer = (CInt(x(i \ 8)) >> (7 - (i Mod 8))) And 1
                If bit = 1 Then
                    For j As Integer = 0 To 15
                        z(j) = CByte(z(j) Xor v(j))
                    Next
                End If
                Dim lsb As Integer = CInt(v(15)) And 1
                Dim carry As Integer = 0
                For j As Integer = 0 To 15
                    Dim current As Integer = CInt(v(j))
                    v(j) = CByte((current >> 1) Or (carry << 7))
                    carry = current And 1
                Next
                If lsb = 1 Then v(0) = CByte(v(0) Xor &HE1)
            Next
            Array.Copy(z, result, 16)
        End Sub

        Private Shared Sub WriteU64(dst As Byte(), offset As Integer, value As Long)
            Dim bits As Long = value * 8L
            For i As Integer = 0 To 7
                dst(offset + 7 - i) = CByte((bits >> (8 * i)) And &HFFL)
            Next
        End Sub

    End Class
End Namespace
