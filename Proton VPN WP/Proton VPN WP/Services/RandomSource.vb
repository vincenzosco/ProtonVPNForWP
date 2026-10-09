' Cryptographic randomness for the SRP client secret ("a").
'
' Kept behind an interface because Windows.Security.Cryptography is WinRT-only and
' the desktop verification harness supplies a fixed value instead.
Imports System.Numerics
Imports Windows.Security.Cryptography
Imports Windows.Storage.Streams

Namespace Services

    Friend Interface IRandomSource
        Function NextBytes(count As Integer) As Byte()
    End Interface

    ''' <summary>WinRT CSPRNG, the production implementation.</summary>
    Friend NotInheritable Class SystemRandomSource
        Implements IRandomSource

        Friend Function NextBytes(count As Integer) As Byte() Implements IRandomSource.NextBytes
            Dim buffer As IBuffer = CryptographicBuffer.GenerateRandom(CUInt(count))
            ' WP8.1's CryptographicBuffer has no ToByteArray, so copy explicitly.
            Dim bytes(count - 1) As Byte
            CryptographicBuffer.CopyToByteArray(buffer, bytes)
            Return bytes
        End Function

    End Class

    Friend NotInheritable Class SrpSecretGenerator

        Private Sub New()
        End Sub

        ''' <summary>
        ''' Picks `a` exactly as go-srp's `generateClientEphemeral` does.
        '''
        ''' go-srp draws `a` in [0, N-1) and accepts it only when it is strictly
        ''' greater than `2 * bitLength` and strictly less than N-1:
        '''
        '''     lowerBoundNat := newNat(uint64(bitLength * 2))
        '''     notTooSmall, _, _ := secret.Cmp(lowerBoundNat)
        '''     if notTooSmall == 1 &amp;&amp; notTooLarge == 1 { break }
        '''
        ''' Note that `bitLength * 2` is a *value* (4096 for 2048-bit SRP), not a bit
        ''' count: `New BigInteger(bitLength * 2)` below is the BigInteger constructed
        ''' from that value, which is what the reference compares against. Treating it
        ''' as a bit length and raising the floor to e.g. 2^16 would reject and re-draw
        ''' secrets that go-srp accepts.
        ''' </summary>
        Friend Shared Function GenerateClientSecret(random As IRandomSource, modulus As Byte(), bitLength As Integer) As BigInteger
            Dim n As BigInteger = Crypto.SrpClient.ToNumber(modulus)
            Dim lowerBound As BigInteger = New BigInteger(bitLength * 2)
            Dim width As Integer = bitLength \ 8

            For attempt As Integer = 1 To 64
                Dim candidate As BigInteger = Crypto.SrpClient.ToNumber(random.NextBytes(width))
                If candidate > lowerBound AndAlso candidate < n - BigInteger.One Then
                    Return candidate
                End If
            Next

            ' Astronomically unlikely; fall back to a value derived from fresh entropy.
            Dim fallback As BigInteger = Crypto.SrpClient.ToNumber(random.NextBytes(width))
            Return If(fallback < BigInteger.One, BigInteger.One, fallback)
        End Function

    End Class
End Namespace
