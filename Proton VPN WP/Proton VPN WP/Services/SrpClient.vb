' Proton SRP-6a, ported from github.com/ProtonMail/go-srp (MIT).
'
' Conventions that must not drift (see the plan's Review Focus):
'   * numbers are exchanged as LITTLE-ENDIAN byte arrays of fixed width
'     (go-srp's toInt/fromInt reverse the bytes),
'   * `expandHash` is SHA-512 with a 0..3 trailing byte, concatenated -> 256 bytes,
'   * the multiplier and the scramble parameter are expandHash(...) reduced mod N,
'   * hashedPassword = expandHash(ascii bcrypt string || modulus).
'
' Randomness is deliberately NOT generated here, so this class stays pure and the
' desktop harness can drive it with fixed values.
Imports System.Numerics

Namespace Crypto
    Friend NotInheritable Class SrpProofs
        Friend Property ClientEphemeral As Byte()
        Friend Property ClientProof As Byte()
        Friend Property ExpectedServerProof As Byte()
        Friend Property SharedSession As Byte()
    End Class

    Friend NotInheritable Class SrpClient

        Private Sub New()
        End Sub

        ''' <summary>
        ''' go-srp `expandHash`: SHA-512(data || 0), SHA-512(data || 1), SHA-512(data || 2),
        ''' SHA-512(data || 3), concatenated => 256 bytes.
        ''' </summary>
        Friend Shared Function ExpandHash(data As Byte()) As Byte()
            Dim result(4 * Sha512.DigestBytes - 1) As Byte
            For i As Integer = 0 To 3
                Dim buffer(data.Length) As Byte
                Array.Copy(data, buffer, data.Length)
                buffer(data.Length) = CByte(i)
                Dim digest As Byte() = Sha512.Hash(buffer)
                Array.Copy(digest, 0, result, i * Sha512.DigestBytes, Sha512.DigestBytes)
            Next
            Return result
        End Function

        ''' <summary>
        ''' go-srp `hashPasswordVersion3` (auth versions 3 and 4):
        ''' expandHash(bcrypt("$2y$10$" + dotSlashBase64(salt || "proton"), password) || modulus).
        ''' </summary>
        Friend Shared Function HashPasswordVersion3(password As Byte(), salt As Byte(), modulus As Byte()) As Byte()
            Dim encodedSalt As String = Bcrypt.EncodeDotSlash(CryptoBytes.Concat(salt, EncodingUtf8("proton")))
            Dim crypted As String = Bcrypt.HashBytes(password, "$2y$10$" & encodedSalt)
            Return ExpandHash(CryptoBytes.Concat(EncodingAscii(crypted), modulus))
        End Function

        Private Shared Function EncodingUtf8(value As String) As Byte()
            Return System.Text.Encoding.UTF8.GetBytes(value)
        End Function

        Private Shared Function EncodingAscii(value As String) As Byte()
            Return System.Text.Encoding.ASCII.GetBytes(value)
        End Function

        ''' <summary>
        ''' go-srp `GenerateProofs`. <paramref name="clientSecret"/> is the client random
        ''' `a`; it is passed in so the caller controls the entropy source.
        ''' </summary>
        Friend Shared Function GenerateProofs(bitLength As Integer, modulus As Byte(),
                                              serverEphemeral As Byte(), hashedPassword As Byte(),
                                              clientSecret As BigInteger) As SrpProofs
            Dim n As BigInteger = ToNumber(modulus)
            Dim b As BigInteger = ToNumber(serverEphemeral)

            If b <= BigInteger.One OrElse b >= n - BigInteger.One Then
                Throw New InvalidOperationException("SRP server ephemeral is out of bounds.")
            End If

            Dim g As BigInteger = New BigInteger(2)
            Dim multiplier As BigInteger = PositiveMod(
                ToNumber(ExpandHash(CryptoBytes.Concat(FromNumber(g, bitLength), modulus))), n)
            If multiplier <= BigInteger.One OrElse multiplier >= n - BigInteger.One Then
                Throw New InvalidOperationException("SRP multiplier is out of bounds.")
            End If

            Dim x As BigInteger = ToNumber(hashedPassword)
            Dim clientEphemeral As Byte() = FromNumber(BigInteger.ModPow(g, clientSecret, n), bitLength)

            Dim scramble As BigInteger = ToNumber(ExpandHash(CryptoBytes.Concat(clientEphemeral, serverEphemeral)))
            If scramble = BigInteger.Zero Then
                Throw New InvalidOperationException("SRP scramble parameter is zero.")
            End If

            Dim baseValue As BigInteger = PositiveMod(b - PositiveMod(multiplier * BigInteger.ModPow(g, x, n), n), n)
            Dim exponent As BigInteger = PositiveMod(scramble * x + clientSecret, n - BigInteger.One)
            Dim sharedValue As BigInteger = BigInteger.ModPow(baseValue, exponent, n)
            Dim sharedSession As Byte() = FromNumber(sharedValue, bitLength)

            Dim clientProof As Byte() = ExpandHash(CryptoBytes.Concat(clientEphemeral, serverEphemeral, sharedSession))
            Dim serverProof As Byte() = ExpandHash(CryptoBytes.Concat(clientEphemeral, clientProof, sharedSession))

            Return New SrpProofs With {
                .ClientEphemeral = clientEphemeral,
                .ClientProof = clientProof,
                .ExpectedServerProof = serverProof,
                .SharedSession = sharedSession
            }
        End Function

        ''' <summary>go-srp `GenerateVerifier`: v = g^x mod N (used to build a server side).</summary>
        Friend Shared Function GenerateVerifier(hashedPassword As Byte(), modulus As Byte()) As BigInteger
            Return BigInteger.ModPow(New BigInteger(2), ToNumber(hashedPassword), ToNumber(modulus))
        End Function

        ''' <summary>
        ''' go-srp `toInt`: the byte array is interpreted LITTLE-ENDIAN.
        ''' (go-srp reverses the array and hands it to big-endian SetBytes, which is
        ''' the same thing.) A trailing zero byte keeps the value non-negative, since
        ''' BigInteger reads the array as two's complement.
        ''' </summary>
        Friend Shared Function ToNumber(data As Byte()) As BigInteger
            Dim littleEndian(data.Length) As Byte
            Array.Copy(data, littleEndian, data.Length)
            Return New BigInteger(littleEndian)
        End Function

        ''' <summary>go-srp `fromInt`: little-endian, zero padded to bitLength/8 bytes.</summary>
        Friend Shared Function FromNumber(value As BigInteger, bitLength As Integer) As Byte()
            Dim width As Integer = bitLength \ 8
            Dim result(width - 1) As Byte
            Dim raw As Byte() = value.ToByteArray()
            Array.Copy(raw, result, Math.Min(raw.Length, width))
            Return result
        End Function

        ''' <summary>Reduction that always returns a non-negative result.</summary>
        Private Shared Function PositiveMod(value As BigInteger, modulus As BigInteger) As BigInteger
            Dim r As BigInteger = value Mod modulus
            If r.Sign < 0 Then r += modulus
            Return r
        End Function

    End Class
End Namespace
