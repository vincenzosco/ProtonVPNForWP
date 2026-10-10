' Pure managed AES-128 (FIPS-197), encryption direction only.
'
' AES-GCM needs only the block cipher's forward direction, so that is all this
' implements. The state is a flat 16-byte array in the RFC's column-major order
' (byte i is row i mod 4 of column i \ 4). Correctness is pinned by the FIPS-197
' C.1 vector and by the GCM vector in the desktop harness.
Namespace Crypto
    Friend NotInheritable Class Aes

        Private Shared ReadOnly SBox As Byte() = {
            &H63, &H7C, &H77, &H7B, &HF2, &H6B, &H6F, &HC5, &H30, &H1, &H67, &H2B, &HFE, &HD7, &HAB, &H76,
            &HCA, &H82, &HC9, &H7D, &HFA, &H59, &H47, &HF0, &HAD, &HD4, &HA2, &HAF, &H9C, &HA4, &H72, &HC0,
            &HB7, &HFD, &H93, &H26, &H36, &H3F, &HF7, &HCC, &H34, &HA5, &HE5, &HF1, &H71, &HD8, &H31, &H15,
            &H4, &HC7, &H23, &HC3, &H18, &H96, &H5, &H9A, &H7, &H12, &H80, &HE2, &HEB, &H27, &HB2, &H75,
            &H9, &H83, &H2C, &H1A, &H1B, &H6E, &H5A, &HA0, &H52, &H3B, &HD6, &HB3, &H29, &HE3, &H2F, &H84,
            &H53, &HD1, &H0, &HED, &H20, &HFC, &HB1, &H5B, &H6A, &HCB, &HBE, &H39, &H4A, &H4C, &H58, &HCF,
            &HD0, &HEF, &HAA, &HFB, &H43, &H4D, &H33, &H85, &H45, &HF9, &H2, &H7F, &H50, &H3C, &H9F, &HA8,
            &H51, &HA3, &H40, &H8F, &H92, &H9D, &H38, &HF5, &HBC, &HB6, &HDA, &H21, &H10, &HFF, &HF3, &HD2,
            &HCD, &HC, &H13, &HEC, &H5F, &H97, &H44, &H17, &HC4, &HA7, &H7E, &H3D, &H64, &H5D, &H19, &H73,
            &H60, &H81, &H4F, &HDC, &H22, &H2A, &H90, &H88, &H46, &HEE, &HB8, &H14, &HDE, &H5E, &HB, &HDB,
            &HE0, &H32, &H3A, &HA, &H49, &H6, &H24, &H5C, &HC2, &HD3, &HAC, &H62, &H91, &H95, &HE4, &H79,
            &HE7, &HC8, &H37, &H6D, &H8D, &HD5, &H4E, &HA9, &H6C, &H56, &HF4, &HEA, &H65, &H7A, &HAE, &H8,
            &HBA, &H78, &H25, &H2E, &H1C, &HA6, &HB4, &HC6, &HE8, &HDD, &H74, &H1F, &H4B, &HBD, &H8B, &H8A,
            &H70, &H3E, &HB5, &H66, &H48, &H3, &HF6, &HE, &H61, &H35, &H57, &HB9, &H86, &HC1, &H1D, &H9E,
            &HE1, &HF8, &H98, &H11, &H69, &HD9, &H8E, &H94, &H9B, &H1E, &H87, &HE9, &HCE, &H55, &H28, &HDF,
            &H8C, &HA1, &H89, &HD, &HBF, &HE6, &H42, &H68, &H41, &H99, &H2D, &HF, &HB0, &H54, &HBB, &H16}

        Private Sub New()
        End Sub

        ''' <summary>Encrypts one 16-byte block under a 16-byte AES-128 key.</summary>
        Friend Shared Function EncryptBlock(key As Byte(), block As Byte()) As Byte()
            If key Is Nothing OrElse key.Length <> 16 Then Throw New System.ArgumentException("AES-128 key must be 16 bytes.")
            If block Is Nothing OrElse block.Length <> 16 Then Throw New System.ArgumentException("AES block must be 16 bytes.")

            Dim rk(175) As Byte
            ExpandKey(key, rk)

            Dim s As Byte() = CType(block.Clone(), Byte())
            AddRoundKey(s, rk, 0)
            For round As Integer = 1 To 9
                SubBytes(s)
                ShiftRows(s)
                MixColumns(s)
                AddRoundKey(s, rk, round)
            Next
            SubBytes(s)
            ShiftRows(s)
            AddRoundKey(s, rk, 10)
            Return s
        End Function

        Private Shared Sub ExpandKey(key As Byte(), rk As Byte())
            Array.Copy(key, rk, 16)
            Dim rcon As Integer = 1
            For i As Integer = 16 To 175 Step 4
                Dim t0 As Byte = rk(i - 4)
                Dim t1 As Byte = rk(i - 3)
                Dim t2 As Byte = rk(i - 2)
                Dim t3 As Byte = rk(i - 1)
                If (i \ 4) Mod 4 = 0 Then
                    ' SubWord(RotWord(temp)) xor Rcon: the round constant XORs the
                    ' FIRST byte of the rotated-and-substituted word, not the last.
                    Dim first As Byte = t0
                    t0 = CByte(SBox(t1) Xor rcon)
                    t1 = SBox(t2)
                    t2 = SBox(t3)
                    t3 = SBox(first)
                    rcon = Xtime(CByte(rcon))
                End If
                rk(i) = CByte(rk(i - 16) Xor t0)
                rk(i + 1) = CByte(rk(i - 15) Xor t1)
                rk(i + 2) = CByte(rk(i - 14) Xor t2)
                rk(i + 3) = CByte(rk(i - 13) Xor t3)
            Next
        End Sub

        Private Shared Sub AddRoundKey(s As Byte(), rk As Byte(), round As Integer)
            For i As Integer = 0 To 15
                s(i) = CByte(s(i) Xor rk(round * 16 + i))
            Next
        End Sub

        Private Shared Sub SubBytes(s As Byte())
            For i As Integer = 0 To 15
                s(i) = SBox(s(i))
            Next
        End Sub

        Private Shared Sub ShiftRows(s As Byte())
            Dim t(15) As Byte
            For c As Integer = 0 To 3
                For r As Integer = 0 To 3
                    t(4 * c + r) = s(4 * ((c + r) Mod 4) + r)
                Next
            Next
            Array.Copy(t, s, 16)
        End Sub

        Private Shared Sub MixColumns(s As Byte())
            For c As Integer = 0 To 3
                Dim i As Integer = c * 4
                Dim a0 As Byte = s(i)
                Dim a1 As Byte = s(i + 1)
                Dim a2 As Byte = s(i + 2)
                Dim a3 As Byte = s(i + 3)
                s(i) = CByte(Xtime(a0) Xor (Xtime(a1) Xor a1) Xor a2 Xor a3)
                s(i + 1) = CByte(a0 Xor Xtime(a1) Xor (Xtime(a2) Xor a2) Xor a3)
                s(i + 2) = CByte(a0 Xor a1 Xor Xtime(a2) Xor (Xtime(a3) Xor a3))
                s(i + 3) = CByte((Xtime(a0) Xor a0) Xor a1 Xor a2 Xor Xtime(a3))
            Next
        End Sub

        ''' <summary>Multiply by x in GF(2^8) modulo the AES polynomial x^8 + x^4 + x^3 + x + 1.</summary>
        Private Shared Function Xtime(b As Byte) As Byte
            Dim v As Integer = CInt(b) << 1
            If (v And &H100) <> 0 Then v = v Xor &H11B
            Return CByte(v And &HFF)
        End Function

    End Class
End Namespace
