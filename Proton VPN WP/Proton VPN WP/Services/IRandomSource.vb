' Cryptographic randomness behind an interface.
'
' Kept in its own file, free of WinRT imports, because the desktop verification
' harness compiles the TLS client and supplies its own implementation: the
' production SystemRandomSource lives in RandomSource.vb, which imports
' Windows.Security.Cryptography and therefore cannot be compiled off-device.
Namespace Services

    Friend Interface IRandomSource
        Function NextBytes(count As Integer) As Byte()
    End Interface

End Namespace
