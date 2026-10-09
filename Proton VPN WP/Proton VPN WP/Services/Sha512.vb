' Pure managed SHA-512 (FIPS 180-4).
'
' Windows Phone 8.1 ships no System.Security.Cryptography, and the WinRT hash API
' is unusable from the shared desktop verification harness, so the algorithm is
' implemented here once and used by both. The harness compares it against the
' Python reference (hashlib), which is what makes the port trustworthy.
'
' NOTE: this file relies on unchecked integer arithmetic (VB checks overflow by
' default). The project sets <RemoveIntegerChecks>true</RemoveIntegerChecks>;
' the desktop harness compiles with /removeintchecks+.
Namespace Crypto
    Friend NotInheritable Class Sha512

        Private Const BlockBytes As Integer = 128
        Public Const DigestBytes As Integer = 64

        ''' <summary>SHA-512 round constants: the first 64 bits of the cube roots of the first 80 primes.</summary>
        Private Shared ReadOnly K As ULong() = {
            &H428A2F98D728AE22UL, &H7137449123EF65CDUL, &HB5C0FBCFEC4D3B2FUL, &HE9B5DBA58189DBBCUL,
            &H3956C25BF348B538UL, &H59F111F1B605D019UL, &H923F82A4AF194F9BUL, &HAB1C5ED5DA6D8118UL,
            &HD807AA98A3030242UL, &H12835B0145706FBEUL, &H243185BE4EE4B28CUL, &H550C7DC3D5FFB4E2UL,
            &H72BE5D74F27B896FUL, &H80DEB1FE3B1696B1UL, &H9BDC06A725C71235UL, &HC19BF174CF692694UL,
            &HE49B69C19EF14AD2UL, &HEFBE4786384F25E3UL, &H0FC19DC68B8CD5B5UL, &H240CA1CC77AC9C65UL,
            &H2DE92C6F592B0275UL, &H4A7484AA6EA6E483UL, &H5CB0A9DCBD41FBD4UL, &H76F988DA831153B5UL,
            &H983E5152EE66DFABUL, &HA831C66D2DB43210UL, &HB00327C898FB213FUL, &HBF597FC7BEEF0EE4UL,
            &HC6E00BF33DA88FC2UL, &HD5A79147930AA725UL, &H06CA6351E003826FUL, &H142929670A0E6E70UL,
            &H27B70A8546D22FFCUL, &H2E1B21385C26C926UL, &H4D2C6DFC5AC42AEDUL, &H53380D139D95B3DFUL,
            &H650A73548BAF63DEUL, &H766A0ABB3C77B2A8UL, &H81C2C92E47EDAEE6UL, &H92722C851482353BUL,
            &HA2BFE8A14CF10364UL, &HA81A664BBC423001UL, &HC24B8B70D0F89791UL, &HC76C51A30654BE30UL,
            &HD192E819D6EF5218UL, &HD69906245565A910UL, &HF40E35855771202AUL, &H106AA07032BBD1B8UL,
            &H19A4C116B8D2D0C8UL, &H1E376C085141AB53UL, &H2748774CDF8EEB99UL, &H34B0BCB5E19B48A8UL,
            &H391C0CB3C5C95A63UL, &H4ED8AA4AE3418ACBUL, &H5B9CCA4F7763E373UL, &H682E6FF3D6B2B8A3UL,
            &H748F82EE5DEFB2FCUL, &H78A5636F43172F60UL, &H84C87814A1F0AB72UL, &H8CC702081A6439ECUL,
            &H90BEFFFA23631E28UL, &HA4506CEBDE82BDE9UL, &HBEF9A3F7B2C67915UL, &HC67178F2E372532BUL,
            &HCA273ECEEA26619CUL, &HD186B8C721C0C207UL, &HEADA7DD6CDE0EB1EUL, &HF57D4F7FEE6ED178UL,
            &H06F067AA72176FBAUL, &H0A637DC5A2C898A6UL, &H113F9804BEF90DAEUL, &H1B710B35131C471BUL,
            &H28DB77F523047D84UL, &H32CAAB7B40C72493UL, &H3C9EBE0A15C9BEBCUL, &H431D67C49C100D4CUL,
            &H4CC5D4BECB3E42B6UL, &H597F299CFC657E2AUL, &H5FCB6FAB3AD6FAECUL, &H6C44198C4A475817UL}

        Private Sub New()
        End Sub

        Friend Shared Function Rotr(x As ULong, n As Integer) As ULong
            Return (x >> n) Or (x << (64 - n))
        End Function

        Private Shared Function Ch(x As ULong, y As ULong, z As ULong) As ULong
            Return (x And y) Xor ((Not x) And z)
        End Function

        Private Shared Function Maj(x As ULong, y As ULong, z As ULong) As ULong
            Return (x And y) Xor (x And z) Xor (y And z)
        End Function

        Private Shared Function BigSigma0(x As ULong) As ULong
            Return Rotr(x, 28) Xor Rotr(x, 34) Xor Rotr(x, 39)
        End Function

        Private Shared Function BigSigma1(x As ULong) As ULong
            Return Rotr(x, 14) Xor Rotr(x, 18) Xor Rotr(x, 41)
        End Function

        Private Shared Function SmallSigma0(x As ULong) As ULong
            Return Rotr(x, 1) Xor Rotr(x, 8) Xor (x >> 7)
        End Function

        Private Shared Function SmallSigma1(x As ULong) As ULong
            Return Rotr(x, 19) Xor Rotr(x, 61) Xor (x >> 6)
        End Function

        ''' <summary>Computes the SHA-512 digest of <paramref name="data"/>.</summary>
        Friend Shared Function Hash(data As Byte()) As Byte()
            If data Is Nothing Then data = New Byte() {}

            Dim h As ULong() = {
                &H6A09E667F3BCC908UL, &HBB67AE8584CAA73BUL, &H3C6EF372FE94F82BUL, &HA54FF53A5F1D36F1UL,
                &H510E527FADE682D1UL, &H9B05688C2B3E6C1FUL, &H1F83D9ABFB41BD6BUL, &H5BE0CD19137E2179UL}

            ' Pad: message || 0x80 || zeros || 128-bit big-endian bit length.
            Dim padded As Integer = ((data.Length + 16) \ BlockBytes + 1) * BlockBytes
            Dim msg(padded - 1) As Byte
            Array.Copy(data, msg, data.Length)
            msg(data.Length) = &H80

            Dim bitLengthHigh As ULong = CULng(data.Length) >> 61
            Dim bitLengthLow As ULong = CULng(data.Length) << 3
            For i As Integer = 0 To 7
                msg(padded - 1 - i) = CByte((bitLengthLow >> (8 * i)) And &HFFUL)
                msg(padded - 9 - i) = CByte((bitLengthHigh >> (8 * i)) And &HFFUL)
            Next

            Dim w(79) As ULong
            For blockStart As Integer = 0 To padded - 1 Step BlockBytes
                For t As Integer = 0 To 15
                    Dim o As Integer = blockStart + t * 8
                    w(t) = (CULng(msg(o)) << 56) Or (CULng(msg(o + 1)) << 48) Or
                           (CULng(msg(o + 2)) << 40) Or (CULng(msg(o + 3)) << 32) Or
                           (CULng(msg(o + 4)) << 24) Or (CULng(msg(o + 5)) << 16) Or
                           (CULng(msg(o + 6)) << 8) Or CULng(msg(o + 7))
                Next
                For t As Integer = 16 To 79
                    w(t) = SmallSigma1(w(t - 2)) + w(t - 7) + SmallSigma0(w(t - 15)) + w(t - 16)
                Next

                Dim a As ULong = h(0), b As ULong = h(1), c As ULong = h(2), d As ULong = h(3)
                Dim e As ULong = h(4), f As ULong = h(5), g As ULong = h(6), hh As ULong = h(7)

                For t As Integer = 0 To 79
                    Dim t1 As ULong = hh + BigSigma1(e) + Ch(e, f, g) + K(t) + w(t)
                    Dim t2 As ULong = BigSigma0(a) + Maj(a, b, c)
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
                For j As Integer = 0 To 7
                    digest(i * 8 + j) = CByte((h(i) >> (56 - 8 * j)) And &HFFUL)
                Next
            Next
            Return digest
        End Function

    End Class
End Namespace
