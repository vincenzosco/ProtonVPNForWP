' Pure managed RSA signature verification: PKCS#1 v1.5 and PSS, both with SHA-256.
'
' Two callers need this and each needs a different scheme: TLS 1.3 signs
' CertificateVerify with rsa_pss_rsae_sha256 (RFC 8446), and the certificate chain
' Proton serves is signed sha256WithRSAEncryption (PKCS#1 v1.5). The modular
' exponentiation reuses System.Numerics.BigInteger, the same primitive SRP already
' depends on.
'
' Verification is public-key work, so no constant-time discipline is required, but
' a signature that decodes to the wrong length or a modulus that is too small is
' rejected rather than padded -- fail closed.
Imports System.Numerics

Namespace Crypto
    Friend NotInheritable Class RsaVerify

        Private Const HLen As Integer = 32
        ''' <summary>The PSS salt length Proton's edge uses with rsa_pss_rsae_sha256.</summary>
        Private Const SLen As Integer = 32

        Private Sub New()
        End Sub

        ''' <summary>RSASSA-PSS-VERIFY with SHA-256 and MGF1-SHA256 (RFC 8017 8.1.2).</summary>
        Friend Shared Function VerifyPssSha256(modulus As Byte(), exponent As Byte(), message As Byte(), signature As Byte()) As Boolean
            Dim em As Byte() = Decode(modulus, exponent, signature)
            If em Is Nothing Then Return False

            Dim emBits As Integer = ModulusBits(modulus) - 1
            Dim emLen As Integer = (emBits + 7) \ 8
            If em.Length <> emLen Then Return False
            If emLen < HLen + SLen + 2 Then Return False
            If em(emLen - 1) <> &HBC Then Return False

            Dim dbLen As Integer = emLen - HLen - 1
            Dim maskedDb(dbLen - 1) As Byte
            Array.Copy(em, 0, maskedDb, 0, dbLen)
            Dim h(HLen - 1) As Byte
            Array.Copy(em, dbLen, h, 0, HLen)

            Dim unusedBits As Integer = 8 * emLen - emBits
            If unusedBits > 0 AndAlso (CInt(maskedDb(0)) >> (8 - unusedBits)) <> 0 Then Return False

            Dim dbMask As Byte() = Mgf1(h, dbLen)
            Dim db(dbLen - 1) As Byte
            For i As Integer = 0 To dbLen - 1
                db(i) = CByte(maskedDb(i) Xor dbMask(i))
            Next
            If unusedBits > 0 Then db(0) = CByte(CInt(db(0)) And (CInt(&HFF) >> unusedBits))

            For i As Integer = 0 To dbLen - SLen - 2
                If db(i) <> 0 Then Return False
            Next
            If db(dbLen - SLen - 1) <> &H1 Then Return False

            Dim salt(SLen - 1) As Byte
            Array.Copy(db, dbLen - SLen, salt, 0, SLen)

            Dim mPrime As Byte() = CryptoBytes.Concat(New Byte(7) {}, Sha256.Hash(message), salt)
            Return CryptoBytes.ConstantTimeEquals(h, Sha256.Hash(mPrime))
        End Function

        ''' <summary>RSASSA-PKCS1-v1_5-VERIFY with SHA-256 (RFC 8017 8.2.2).</summary>
        Friend Shared Function VerifyPkcs1Sha256(modulus As Byte(), exponent As Byte(), message As Byte(), signature As Byte()) As Boolean
            Dim em As Byte() = Decode(modulus, exponent, signature)
            If em Is Nothing Then Return False
            If em.Length < 11 Then Return False
            If em(0) <> 0 OrElse em(1) <> 1 Then Return False

            Dim i As Integer = 2
            While i < em.Length AndAlso em(i) = &HFF
                i += 1
            End While
            If i < 10 Then Return False
            If i >= em.Length OrElse em(i) <> 0 Then Return False
            i += 1

            Dim digestInfo As Byte() = CryptoBytes.Concat(DigestInfoPrefix(), Sha256.Hash(message))
            If em.Length - i <> digestInfo.Length Then Return False
            Dim candidate(digestInfo.Length - 1) As Byte
            Array.Copy(em, i, candidate, 0, digestInfo.Length)
            Return CryptoBytes.ConstantTimeEquals(candidate, digestInfo)
        End Function

        ''' <summary>The DER DigestInfo header for SHA-256, without the digest.</summary>
        Private Shared Function DigestInfoPrefix() As Byte()
            Return New Byte() {&H30, &H31, &H30, &HD, &H6, &H9, &H60, &H86, &H48, &H1, &H65, &H3, &H4, &H2, &H1, &H5, &H0, &H4, &H20}
        End Function

        ''' <summary>RSAVP1: signature^exponent mod modulus, as a fixed-length octet string.</summary>
        Private Shared Function Decode(modulus As Byte(), exponent As Byte(), signature As Byte()) As Byte()
            If modulus Is Nothing OrElse modulus.Length = 0 Then Return Nothing
            If exponent Is Nothing OrElse exponent.Length = 0 Then Return Nothing
            If signature Is Nothing OrElse signature.Length <> modulus.Length Then Return Nothing

            Dim n As BigInteger = ToPositive(modulus)
            Dim e As BigInteger = ToPositive(exponent)
            Dim s As BigInteger = ToPositive(signature)
            If s >= n Then Return Nothing

            Return FixedLength(BigInteger.ModPow(s, e, n), modulus.Length)
        End Function

        ''' <summary>Big-endian bytes to a positive BigInteger.</summary>
        Private Shared Function ToPositive(data As Byte()) As BigInteger
            Dim le(data.Length) As Byte
            For i As Integer = 0 To data.Length - 1
                le(i) = data(data.Length - 1 - i)
            Next
            le(data.Length) = 0
            Return New BigInteger(le)
        End Function

        ''' <summary>I2OSP: a BigInteger as exactly <paramref name="length"/> big-endian bytes.</summary>
        Private Shared Function FixedLength(value As BigInteger, length As Integer) As Byte()
            Dim le As Byte() = value.ToByteArray()
            If le.Length > length Then
                ' Only a leading sign byte is acceptable.
                For i As Integer = length To le.Length - 1
                    If le(i) <> 0 Then Return Nothing
                Next
            End If
            Dim result(length - 1) As Byte
            Dim n As Integer = System.Math.Min(le.Length, length)
            For i As Integer = 0 To n - 1
                result(length - 1 - i) = le(i)
            Next
            Return result
        End Function

        Private Shared Function ModulusBits(modulus As Byte()) As Integer
            Dim n As BigInteger = ToPositive(modulus)
            Dim bits As Integer = 0
            While n > BigInteger.Zero
                n = n >> 1
                bits += 1
            End While
            Return bits
        End Function

        ''' <summary>MGF1 with SHA-256 (RFC 8017 B.2.1).</summary>
        Private Shared Function Mgf1(seed As Byte(), length As Integer) As Byte()
            Dim mask(length - 1) As Byte
            Dim offset As Integer = 0
            Dim counter As Integer = 0
            While offset < length
                Dim c As Byte() = {CByte((counter >> 24) And &HFF), CByte((counter >> 16) And &HFF),
                                   CByte((counter >> 8) And &HFF), CByte(counter And &HFF)}
                Dim block As Byte() = Sha256.Hash(CryptoBytes.Concat(seed, c))
                Dim n As Integer = System.Math.Min(block.Length, length - offset)
                Array.Copy(block, 0, mask, offset, n)
                offset += n
                counter += 1
            End While
            Return mask
        End Function

    End Class
End Namespace
