' Pure managed HMAC (RFC 2104) over SHA-256.
'
' Used by HKDF (TLS 1.3 key schedule). The block size is SHA-256's, and the
' construction is H(K ^ opad || H(K ^ ipad || data)); a key longer than the block
' is hashed first, per the RFC.
Namespace Crypto
    Friend NotInheritable Class Hmac

        Private Const BlockBytes As Integer = 64

        Private Sub New()
        End Sub

        ' NOTE: the type is qualified as Global.Crypto.Sha256 because this class has
        ' a member named Sha256 too, and inside the class that member wins name
        ' resolution -- an unqualified `Sha256.Hash` binds to the method's return
        ' type (Byte() = System.Array) and fails BC30456.
        Friend Shared Function Sha256(key As Byte(), data As Byte()) As Byte()
            If data Is Nothing Then data = New Byte() {}

            Dim k(BlockBytes - 1) As Byte
            If key IsNot Nothing Then
                If key.Length > BlockBytes Then
                    Dim hashed As Byte() = Global.Crypto.Sha256.Hash(key)
                    Array.Copy(hashed, k, hashed.Length)
                Else
                    Array.Copy(key, k, key.Length)
                End If
            End If

            Dim ipad(BlockBytes - 1) As Byte
            Dim opad(BlockBytes - 1) As Byte
            For i As Integer = 0 To BlockBytes - 1
                ipad(i) = CByte(CInt(k(i)) Xor &H36)
                opad(i) = CByte(CInt(k(i)) Xor &H5C)
            Next

            Dim inner As Byte() = Global.Crypto.Sha256.Hash(CryptoBytes.Concat(ipad, data))
            Return Global.Crypto.Sha256.Hash(CryptoBytes.Concat(opad, inner))
        End Function

    End Class
End Namespace
