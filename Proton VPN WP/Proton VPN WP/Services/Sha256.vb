' Pure managed SHA-256 (FIPS 180-4).
'
' Same reason as Sha512.vb: Windows Phone 8.1 ships no usable
' System.Security.Cryptography, so the algorithm is implemented here once and used
' by both the app and the desktop verification harness (which compares it against
' Python's hashlib).
'
' NOTE: relies on unchecked integer arithmetic; the project sets
' <RemoveIntegerChecks>true</RemoveIntegerChecks> and the harness compiles with
' /removeintchecks+.
Namespace Crypto
    Friend NotInheritable Class Sha256

        Private Const BlockBytes As Integer = 64
        Public Const DigestBytes As Integer = 32

        ''' <summary>Round constants: the first 32 bits of the cube roots of the first 64 primes.</summary>
        Private Shared ReadOnly K As UInteger() = {
            &H428A2F98UI, &H71374491UI, &HB5C0FBCFUI, &HE9B5DBA5UI,
            &H3956C25BUI, &H59F111F1UI, &H923F82A4UI, &HAB1C5ED5UI,
            &HD807AA98UI, &H12835B01UI, &H243185BEUI, &H550C7DC3UI,
            &H72BE5D74UI, &H80DEB1FEUI, &H9BDC06A7UI, &HC19BF174UI,
            &HE49B69C1UI, &HEFBE4786UI, &H0FC19DC6UI, &H240CA1CCUI,
            &H2DE92C6FUI, &H4A7484AAUI, &H5CB0A9DCUI, &H76F988DAUI,
            &H983E5152UI, &HA831C66DUI, &HB00327C8UI, &HBF597FC7UI,
            &HC6E00BF3UI, &HD5A79147UI, &H06CA6351UI, &H14292967UI,
            &H27B70A85UI, &H2E1B2138UI, &H4D2C6DFCUI, &H53380D13UI,
            &H650A7354UI, &H766A0ABBUI, &H81C2C92EUI, &H92722C85UI,
            &HA2BFE8A1UI, &HA81A664BUI, &HC24B8B70UI, &HC76C51A3UI,
            &HD192E819UI, &HD6990624UI, &HF40E3585UI, &H106AA070UI,
            &H19A4C116UI, &H1E376C08UI, &H2748774CUI, &H34B0BCB5UI,
            &H391C0CB3UI, &H4ED8AA4AUI, &H5B9CCA4FUI, &H682E6FF3UI,
            &H748F82EEUI, &H78A5636FUI, &H84C87814UI, &H8CC70208UI,
            &H90BEFFFAUI, &HA4506CEBUI, &HBEF9A3F7UI, &HC67178F2UI}

        Private Sub New()
        End Sub

        Private Shared Function Rotr(x As UInteger, n As Integer) As UInteger
            Return (x >> n) Or (x << (32 - n))
        End Function

        Private Shared Function Ch(x As UInteger, y As UInteger, z As UInteger) As UInteger
            Return (x And y) Xor ((Not x) And z)
        End Function

        Private Shared Function Maj(x As UInteger, y As UInteger, z As UInteger) As UInteger
            Return (x And y) Xor (x And z) Xor (y And z)
        End Function

        Private Shared Function BigSigma0(x As UInteger) As UInteger
            Return Rotr(x, 2) Xor Rotr(x, 13) Xor Rotr(x, 22)
        End Function

        Private Shared Function BigSigma1(x As UInteger) As UInteger
            Return Rotr(x, 6) Xor Rotr(x, 11) Xor Rotr(x, 25)
        End Function

        Private Shared Function SmallSigma0(x As UInteger) As UInteger
            Return Rotr(x, 7) Xor Rotr(x, 18) Xor (x >> 3)
        End Function

        Private Shared Function SmallSigma1(x As UInteger) As UInteger
            Return Rotr(x, 17) Xor Rotr(x, 19) Xor (x >> 10)
        End Function

        ''' <summary>Computes the SHA-256 digest of <paramref name="data"/>.</summary>
        Friend Shared Function Hash(data As Byte()) As Byte()
            If data Is Nothing Then data = New Byte() {}

            Dim h As UInteger() = {&H6A09E667UI, &HBB67AE85UI, &H3C6EF372UI, &HA54FF53AUI,
                                   &H510E527FUI, &H9B05688CUI, &H1F83D9ABUI, &H5BE0CD19UI}

            ' Pad: message || 0x80 || zeros || 64-bit big-endian bit length.
            Dim padded As Integer = ((data.Length + 8) \ BlockBytes + 1) * BlockBytes
            Dim msg(padded - 1) As Byte
            Array.Copy(data, msg, data.Length)
            msg(data.Length) = &H80

            Dim bitLength As ULong = CULng(data.Length) * 8UL
            For i As Integer = 0 To 7
                msg(padded - 1 - i) = CByte((bitLength >> (8 * i)) And &HFFUL)
            Next

            Dim w(63) As UInteger
            For blockStart As Integer = 0 To padded - 1 Step BlockBytes
                For t As Integer = 0 To 15
                    Dim o As Integer = blockStart + t * 4
                    w(t) = (CUInt(msg(o)) << 24) Or (CUInt(msg(o + 1)) << 16) Or
                           (CUInt(msg(o + 2)) << 8) Or CUInt(msg(o + 3))
                Next
                For t As Integer = 16 To 63
                    w(t) = SmallSigma1(w(t - 2)) + w(t - 7) + SmallSigma0(w(t - 15)) + w(t - 16)
                Next

                Dim a As UInteger = h(0), b As UInteger = h(1), c As UInteger = h(2), d As UInteger = h(3)
                Dim e As UInteger = h(4), f As UInteger = h(5), g As UInteger = h(6), hh As UInteger = h(7)

                For t As Integer = 0 To 63
                    Dim t1 As UInteger = hh + BigSigma1(e) + Ch(e, f, g) + K(t) + w(t)
                    Dim t2 As UInteger = BigSigma0(a) + Maj(a, b, c)
                    hh = g
                    g = f
                    f = e
                    e = d + t1
                    d = c
                    c = b
                    b = a
                    a = t1 + t2
                Next

                h(0) += a
                h(1) += b
                h(2) += c
                h(3) += d
                h(4) += e
                h(5) += f
                h(6) += g
                h(7) += hh
            Next

            Dim digest(DigestBytes - 1) As Byte
            For i As Integer = 0 To 7
                For j As Integer = 0 To 3
                    digest(i * 4 + j) = CByte((h(i) >> (24 - 8 * j)) And &HFFUI)
                Next
            Next
            Return digest
        End Function

    End Class
End Namespace
