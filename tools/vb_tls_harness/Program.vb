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
Imports Tls

Module Program

    Sub Main(args As String())
        Try
            If args.Length >= 6 AndAlso args(0) = "rsa" Then
                RunRsa(args)
                Environment.Exit(0)
            End If

            If args.Length >= 3 AndAlso args(0) = "keys" Then
                RunKeys(args)
                Environment.Exit(0)
            End If

            If args.Length >= 3 AndAlso args(0) = "chain" Then
                RunChain(args)
                Environment.Exit(0)
            End If

            Console.WriteLine("sha256_empty=" & CryptoBytes.ToHex(Sha256.Hash(New Byte() {})))
            Console.WriteLine("sha256_abc=" & CryptoBytes.ToHex(Sha256.Hash(System.Text.Encoding.ASCII.GetBytes("abc"))))

            Dim key As Byte() = New Byte(19) {}
            For i As Integer = 0 To 19
                key(i) = &HBB
            Next
            Console.WriteLine("hmac_sha256=" & CryptoBytes.ToHex(Hmac.Compute(key, System.Text.Encoding.ASCII.GetBytes("Hi There"))))

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

            ' FIPS-197 C.1 and GCM Test Case 2.
            Dim aesKey As Byte() = CryptoBytes.FromHex("000102030405060708090a0b0c0d0e0f")
            Dim aesBlock As Byte() = CryptoBytes.FromHex("00112233445566778899aabbccddeeff")
            Console.WriteLine("aes_block=" & CryptoBytes.ToHex(Aes.EncryptBlock(aesKey, aesBlock)))

            Dim gcmKey As Byte() = New Byte(15) {}
            Dim gcmIv As Byte() = New Byte(11) {}
            Dim gcmPlain As Byte() = New Byte(15) {}
            Console.WriteLine("gcm_sealed=" & CryptoBytes.ToHex(Gcm.Seal(gcmKey, gcmIv, New Byte() {}, gcmPlain)))

            Environment.Exit(0)
        Catch ex As Exception
            Console.Error.WriteLine("HARNESS FAILURE: " & ex.GetType().Name & ": " & ex.Message)
            Console.Error.WriteLine(ex.StackTrace)
            Environment.Exit(1)
        End Try
    End Sub

    ' rsa <modulusHex> <exponentHex> <messageHex> <pssSignatureHex> <pkcs1SignatureHex>
    Sub RunRsa(args As String())
        Dim modulus As Byte() = CryptoBytes.FromHex(args(1))
        Dim exponent As Byte() = CryptoBytes.FromHex(args(2))
        Dim message As Byte() = CryptoBytes.FromHex(args(3))
        Dim pss As Byte() = CryptoBytes.FromHex(args(4))
        Dim pkcs1 As Byte() = CryptoBytes.FromHex(args(5))

        Console.WriteLine("pss_ok=" & If(RsaVerify.VerifyPssSha256(modulus, exponent, message, pss), "1", "0"))
        Console.WriteLine("pkcs1_ok=" & If(RsaVerify.VerifyPkcs1Sha256(modulus, exponent, message, pkcs1), "1", "0"))

        Dim tampered As Byte() = CType(pss.Clone(), Byte())
        tampered(tampered.Length - 1) = CByte(tampered(tampered.Length - 1) Xor 1)
        Console.WriteLine("pss_reject_flip=" & If(RsaVerify.VerifyPssSha256(modulus, exponent, message, tampered), "0", "1"))

        Dim wrongMessage As Byte() = System.Text.Encoding.ASCII.GetBytes("another-message")
        Console.WriteLine("pkcs1_reject_msg=" & If(RsaVerify.VerifyPkcs1Sha256(modulus, exponent, wrongMessage, pkcs1), "0", "1"))
    End Sub

    ' keys <sharedSecretHex> <transcriptHashHex>
    Sub RunKeys(args As String())
        Dim sharedSecret As Byte() = CryptoBytes.FromHex(args(1))
        Dim th As Byte() = CryptoBytes.FromHex(args(2))

        Dim early As Byte() = TlsKeys.EarlySecret()
        Dim handshakeSecret As Byte() = TlsKeys.HandshakeSecret(early, sharedSecret)
        Dim master As Byte() = TlsKeys.MasterSecret(handshakeSecret)
        Dim cHs As Byte() = TlsKeys.ClientHandshakeTrafficSecret(handshakeSecret, th)

        Console.WriteLine("early=" & CryptoBytes.ToHex(early))
        Console.WriteLine("handshake=" & CryptoBytes.ToHex(handshakeSecret))
        Console.WriteLine("master=" & CryptoBytes.ToHex(master))
        Console.WriteLine("c_hs=" & CryptoBytes.ToHex(cHs))
        Console.WriteLine("s_hs=" & CryptoBytes.ToHex(TlsKeys.ServerHandshakeTrafficSecret(handshakeSecret, th)))
        Console.WriteLine("c_ap=" & CryptoBytes.ToHex(TlsKeys.ClientApplicationTrafficSecret(master, th)))
        Console.WriteLine("s_ap=" & CryptoBytes.ToHex(TlsKeys.ServerApplicationTrafficSecret(master, th)))
        Console.WriteLine("key=" & CryptoBytes.ToHex(TlsKeys.TrafficKey(cHs)))
        Console.WriteLine("iv=" & CryptoBytes.ToHex(TlsKeys.TrafficIv(cHs)))
        Console.WriteLine("finished=" & CryptoBytes.ToHex(TlsKeys.FinishedVerifyData(TlsKeys.FinishedKey(cHs), th)))
    End Sub

    ' chain <certDir> <host>
    Sub RunChain(args As String())
        Dim certDir As String = args(1)
        Dim host As String = args(2)
        Dim now As New DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc)

        Dim chain As New System.Collections.Generic.List(Of X509.Certificate)()
        For i As Integer = 1 To 3
            chain.Add(X509.Parse(System.IO.File.ReadAllBytes(
                System.IO.Path.Combine(certDir, "cert" & i.ToString() & ".der"))))
        Next

        Dim reason As String = X509.VerifyChain(chain, host, now)
        Console.WriteLine("chain_ok=" & If(reason Is Nothing, "1", "0"))
        If reason IsNot Nothing Then Console.WriteLine("chain_reason=" & reason)

        Console.WriteLine("chain_bad_host=" & If(X509.VerifyChain(chain, "evil.example.com", now) Is Nothing, "0", "1"))

        ' Tamper with the leaf signature and expect the chain to be rejected.
        Dim tampered As New System.Collections.Generic.List(Of X509.Certificate)()
        Dim leaf As X509.Certificate = X509.Parse(System.IO.File.ReadAllBytes(
            System.IO.Path.Combine(certDir, "cert1.der")))
        Dim broken As Byte() = CType(leaf.Signature.Clone(), Byte())
        broken(0) = CByte(broken(0) Xor 1)
        leaf.Signature = broken
        tampered.Add(leaf)
        tampered.Add(chain(1))
        tampered.Add(chain(2))
        Console.WriteLine("chain_bad_sig=" & If(X509.VerifyChain(tampered, host, now) Is Nothing, "0", "1"))
    End Sub

End Module
