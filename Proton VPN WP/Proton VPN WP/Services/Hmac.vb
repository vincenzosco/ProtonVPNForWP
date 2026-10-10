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

        ' The hash is taken through a distinct member name (Compute, not Sha256)
        ' because the project's RootNamespace makes the type Proton_VPN_WP.Crypto.Sha256
        ' while the desktop harness compiles it as Crypto.Sha256: a member named
        ' Sha256 would shadow the type inside this class in both, and no single
        ' qualifier names the type in both compilations.
        Friend Shared Function Compute(key As Byte(), data As Byte()) As Byte()
            If data Is Nothing Then data = New Byte() {}

            Dim k(BlockBytes - 1) As Byte
            If key IsNot Nothing Then
                If key.Length > BlockBytes Then
                    Dim hashed As Byte() = Sha256.Hash(key)
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

            Dim inner As Byte() = Sha256.Hash(CryptoBytes.Concat(ipad, data))
            Return Sha256.Hash(CryptoBytes.Concat(opad, inner))
        End Function

    End Class
End Namespace
