' Verification harness for the SHIPPING TLS crypto source.
'
' This file is compiled together with the app's own Services\*.vb files, so it
' exercises the exact code that ships in the phone app -- not a reimplementation
' (the same pattern as tools\vb_srp_harness\Program.vb).
'
' Usage:
'   Program.exe            # crypto vectors
' Prints one `key=hex` line per value for the Python driver to compare.
Imports System
Imports Crypto

Module Program

    Sub Main(args As String())
        Try
            Console.WriteLine("sha256_empty=" & CryptoBytes.ToHex(Sha256.Hash(New Byte() {})))
            Console.WriteLine("sha256_abc=" & CryptoBytes.ToHex(Sha256.Hash(System.Text.Encoding.ASCII.GetBytes("abc"))))

            Dim key As Byte() = New Byte(19) {}
            For i As Integer = 0 To 19
                key(i) = &HBB
            Next
            Console.WriteLine("hmac_sha256=" & CryptoBytes.ToHex(Hmac.Sha256(key, System.Text.Encoding.ASCII.GetBytes("Hi There"))))

            ' RFC 5869 Test Case 1.
            Dim ikm As Byte() = New Byte(21) {}
            For i As Integer = 0 To 21
                ikm(i) = &HB
            Next
            Dim salt As Byte() = {&H0, &H1, &H2, &H3, &H4, &H5, &H6, &H7, &H8, &H9, &HA, &HB, &HC}
            Dim info As Byte() = {&HF0, &HF1, &HF2, &HF3, &HF4, &HF5, &HF6, &HF7, &HF8, &HF9}
            Dim prk As Byte() = Hkdf.Extract(salt, ikm)
            Console.WriteLine("hkdf_prk=" & CryptoBytes.ToHex(prk))
            Console.WriteLine("hkdf_okm=" & CryptoBytes.ToHex(Hkdf.Expand(prk, info, 42)))

            Environment.Exit(0)
        Catch ex As Exception
            Console.Error.WriteLine("HARNESS FAILURE: " & ex.GetType().Name & ": " & ex.Message)
            Environment.Exit(1)
        End Try
    End Sub

End Module
