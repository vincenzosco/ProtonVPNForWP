' Verification harness for the SHIPPING SRP/bcrypt/SHA-512 source.
'
' This file is compiled together with the app's own Services\*.vb files, so it
' exercises the exact code that ships in the phone app -- not a reimplementation.
'
' Usage:
'   Program.exe <modulusHex> <serverEphemeralHex> <password> <saltBase64> <clientSecretHex>
'
' Prints one `key=hex` line per value for the Python driver to compare.
Imports System
Imports Crypto

Module Program

    Sub Main(args As String())
        If args.Length < 5 Then
            Console.Error.WriteLine("usage: Program <modulusHex> <serverEphemeralHex> <password> <saltBase64> <clientSecretHex>")
            Environment.Exit(2)
        End If

        Try
            Dim modulus As Byte() = CryptoBytes.FromHex(args(0))
            Dim serverEphemeral As Byte() = CryptoBytes.FromHex(args(1))
            Dim password As Byte() = System.Text.Encoding.UTF8.GetBytes(args(2))
            Dim salt As Byte() = Convert.FromBase64String(args(3))
            Dim clientSecret As System.Numerics.BigInteger = SrpClient.ToNumber(CryptoBytes.FromHex(args(4)))

            Console.WriteLine("sha512_empty=" & CryptoBytes.ToHex(Sha512.Hash(New Byte() {})))
            Console.WriteLine("sha512_abc=" & CryptoBytes.ToHex(Sha512.Hash(System.Text.Encoding.ASCII.GetBytes("abc"))))

            Dim encodedSalt As String = Bcrypt.EncodeDotSlash(
                CryptoBytes.Concat(salt, System.Text.Encoding.UTF8.GetBytes("proton")))
            Console.WriteLine("encoded_salt=" & encodedSalt)
            Console.WriteLine("bcrypt_string=" & Bcrypt.HashBytes(password, "$2y$10$" & encodedSalt))

            Dim hashed As Byte() = SrpClient.HashPasswordVersion3(password, salt, modulus)
            Console.WriteLine("hashed_password=" & CryptoBytes.ToHex(hashed))

            Dim proofs As SrpProofs = SrpClient.GenerateProofs(2048, modulus, serverEphemeral, hashed, clientSecret)
            Console.WriteLine("client_ephemeral=" & CryptoBytes.ToHex(proofs.ClientEphemeral))
            Console.WriteLine("client_proof=" & CryptoBytes.ToHex(proofs.ClientProof))
            Console.WriteLine("server_proof=" & CryptoBytes.ToHex(proofs.ExpectedServerProof))
            Console.WriteLine("shared_session=" & CryptoBytes.ToHex(proofs.SharedSession))

            Environment.Exit(0)
        Catch ex As Exception
            Console.Error.WriteLine("HARNESS FAILURE: " & ex.GetType().Name & ": " & ex.Message)
            Environment.Exit(1)
        End Try
    End Sub

End Module
