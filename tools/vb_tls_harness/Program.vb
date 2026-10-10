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

            Environment.Exit(0)
        Catch ex As Exception
            Console.Error.WriteLine("HARNESS FAILURE: " & ex.GetType().Name & ": " & ex.Message)
            Environment.Exit(1)
        End Try
    End Sub

End Module
