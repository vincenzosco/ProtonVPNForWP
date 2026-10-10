' Pure managed X25519 (RFC 7748), the key exchange TLS 1.3 uses here.
'
' Proton's edge accepts the x25519 group, and Windows Phone 8.1 offers no curve
' arithmetic, so the Montgomery ladder over Curve25519 is implemented here. The
' field is p = 2^255 - 19; the scalar is clamped to a multiple of the cofactor as
' the RFC requires. Correctness is pinned by the RFC 7748 vectors in the desktop
' harness, not by inspection.
Imports System.Numerics

Namespace Crypto
    Friend NotInheritable Class X25519

        Private Shared ReadOnly P As BigInteger = BigInteger.Pow(2, 255) - BigInteger.One * 19
        ''' <summary>(486662 - 2) / 4, the RFC 7748 a24 constant for Curve25519.</summary>
        Private Shared ReadOnly A24 As BigInteger = New BigInteger(121665)

        Private Sub New()
        End Sub

        ''' <summary>The public key for a private scalar: X25519(scalar, base point 9).</summary>
        Friend Shared Function PublicFromPrivate(scalar As Byte()) As Byte()
            Return SharedSecret(scalar, BasePoint())
        End Function

        ''' <summary>The shared u-coordinate: X25519(scalar, peerPublic).</summary>
        Friend Shared Function SharedSecret(scalar As Byte(), peerPublic As Byte()) As Byte()
            If scalar Is Nothing OrElse scalar.Length <> 32 Then Throw New System.ArgumentException("X25519 scalar must be 32 bytes.")
            If peerPublic Is Nothing OrElse peerPublic.Length <> 32 Then Throw New System.ArgumentException("X25519 public key must be 32 bytes.")

            Dim k As Byte() = CType(scalar.Clone(), Byte())
            ' Clamp: clear the low 3 bits, clear the top bit, set the second-highest.
            k(0) = CByte(k(0) And &HF8)
            k(31) = CByte(k(31) And &H7F)
            k(31) = CByte(k(31) Or &H40)

            Dim x1 As BigInteger = DecodeU(peerPublic)
            Dim x2 As BigInteger = BigInteger.One
            Dim z2 As BigInteger = BigInteger.Zero
            Dim x3 As BigInteger = x1
            Dim z3 As BigInteger = BigInteger.One
            Dim swap As Integer = 0

            For t As Integer = 254 To 0 Step -1
                Dim kt As Integer = CInt((CInt(k(t >> 3)) >> (t And 7)) And 1)
                swap = swap Xor kt
                If swap = 1 Then
                    Dim tx As BigInteger = x2
                    x2 = x3
                    x3 = tx
                    Dim tz As BigInteger = z2
                    z2 = z3
                    z3 = tz
                End If
                swap = kt

                Dim a As BigInteger = (x2 + z2) Mod P
                Dim aa As BigInteger = (a * a) Mod P
                Dim b As BigInteger = (x2 - z2 + P) Mod P
                Dim bb As BigInteger = (b * b) Mod P
                Dim e As BigInteger = (aa - bb + P) Mod P
                Dim c As BigInteger = (x3 + z3) Mod P
                Dim d As BigInteger = (x3 - z3 + P) Mod P
                Dim da As BigInteger = (d * a) Mod P
                Dim cb As BigInteger = (c * b) Mod P
                x3 = (((da + cb) Mod P) * ((da + cb) Mod P)) Mod P
                Dim daMinusCb As BigInteger = (da - cb + P) Mod P
                z3 = (x1 * daMinusCb * daMinusCb) Mod P
                x2 = (aa * bb) Mod P
                z2 = (e * ((aa + A24 * e) Mod P)) Mod P
            Next

            If swap = 1 Then
                Dim tx As BigInteger = x2
                x2 = x3
                x3 = tx
                Dim tz As BigInteger = z2
                z2 = z3
                z3 = tz
            End If

            Dim result As BigInteger = (x2 * BigInteger.ModPow(z2, P - 2, P)) Mod P
            Return EncodeU(result)
        End Function

        Private Shared Function BasePoint() As Byte()
            Dim b(31) As Byte
            b(0) = 9
            Return b
        End Function

        ''' <summary>
        ''' Little-endian decode with the top bit masked off. The extra zero byte makes
        ''' the value positive: .NET's BigInteger treats a byte array as two's complement.
        ''' </summary>
        Private Shared Function DecodeU(data As Byte()) As BigInteger
            Dim le(32) As Byte
            Array.Copy(data, le, 32)
            le(31) = CByte(le(31) And &H7F)
            Return New BigInteger(le)
        End Function

        Private Shared Function EncodeU(value As BigInteger) As Byte()
            Dim le(32) As Byte
            Dim bytes As Byte() = value.ToByteArray()
            Array.Copy(bytes, le, System.Math.Min(bytes.Length, 33))
            Dim result(31) As Byte
            Array.Copy(le, result, 32)
            Return result
        End Function

    End Class
End Namespace
