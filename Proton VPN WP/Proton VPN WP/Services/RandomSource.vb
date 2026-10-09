' Cryptographic randomness for the SRP client secret ("a").
'
' Kept behind an interface because Windows.Security.Cryptography is WinRT-only and
' the desktop verification harness supplies a fixed value instead.
Imports System.Numerics
Imports Windows.Security.Cryptography

Namespace Services

    Friend Interface IRandomSource
        Function NextBytes(count As Integer) As Byte()
    End Interface

    ''' <summary>WinRT CSPRNG, the production implementation.</summary>
    Friend NotInheritable Class SystemRandomSource
        Implements IRandomSource

        Friend Function NextBytes(count As Integer) As Byte() Implements IRandomSource.NextBytes
            Dim buffer As IBuffer = CryptographicBuffer.GenerateRandom(CUInt(count))
            Return CryptographicBuffer.ToByteArray(buffer)
        End Function

    End Class

    Friend NotInheritable Class SrpSecretGenerator

        Private Sub New()
        End Sub

        ''' <summary>
        ''' go-srp picks `a` in [1, N-1) and rejects values below 2*bitLength. We do
        ''' the same so the ephemeral is never degenerate.
        ''' </summary>
        Friend Shared Function GenerateClientSecret(random As IRandomSource, modulus As Byte(), bitLength As Integer) As BigInteger
            Dim n As BigInteger = Crypto.SrpClient.ToNumber(modulus)
            Dim lowerBound As BigInteger = New BigInteger(bitLength * 2)
            Dim width As Integer = bitLength \ 8

            For attempt As Integer = 1 To 64
                Dim candidate As BigInteger = Crypto.SrpClient.ToNumber(random.NextBytes(width))
                If candidate >= lowerBound AndAlso candidate < n - BigInteger.One Then
                    Return candidate
                End If
            Next

            ' Astronomically unlikely; fall back to a value derived from fresh entropy.
            Dim fallback As BigInteger = Crypto.SrpClient.ToNumber(random.NextBytes(width))
            Return If(fallback < BigInteger.One, BigInteger.One, fallback)
        End Function

    End Class
End Namespace
