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

            ' RFC 7748 section 5.2 and section 6.1.
            Dim vecScalar As Byte() = CryptoBytes.FromHex("a546e36bf0527c9d3b16154b82465edd62144c0ac1fc5a18506a2244ba449ac4")
            Dim vecU As Byte() = CryptoBytes.FromHex("e6db6867583030db3594c1a424b15f7c726624ec26b3353b10a903a6d0ab1c4c")
            Console.WriteLine("x25519_vec=" & CryptoBytes.ToHex(X25519.SharedSecret(vecScalar, vecU)))

            Dim alicePriv As Byte() = CryptoBytes.FromHex("77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a")
            Dim bobPub As Byte() = CryptoBytes.FromHex("de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f")
            Console.WriteLine("x25519_pub=" & CryptoBytes.ToHex(X25519.PublicFromPrivate(alicePriv)))
            Console.WriteLine("x25519_shared=" & CryptoBytes.ToHex(X25519.SharedSecret(alicePriv, bobPub)))

            Environment.Exit(0)
        Catch ex As Exception
            Console.Error.WriteLine("HARNESS FAILURE: " & ex.GetType().Name & ": " & ex.Message)
            Environment.Exit(1)
        End Try
    End Sub

End Module
