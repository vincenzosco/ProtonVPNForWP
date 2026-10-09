' bcrypt, matching Proton's `github.com/ProtonMail/bcrypt` fork.
'
' Proton calls `bcrypt.HashBytes(password, "$2y$10$" + salt)` and consumes the
' returned 60 character string (NOT the raw digest) in `hashPasswordVersion3`.
' Getting that wrong silently breaks authentication, so the desktop harness
' compares this implementation against python-bcrypt on several inputs.
'
' Requires unchecked integer arithmetic (see the note in Sha512.vb).
Imports System.Collections.Generic
Imports System.Globalization
Imports System.Text

Namespace Crypto
    Friend NotInheritable Class Bcrypt

        ''' <summary>bcrypt's own base64 alphabet (dot-slash, no padding).</summary>
        Private Const DotSlash As String = "./ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789"

        Private Const WordMask As UInteger = &HFFFFFFFFUI
        Private Const HashBytesEncoded As Integer = 23 ' bcrypt only encodes 23 of the 24 bytes

        ''' <summary>The maximum number of password bytes bcrypt will consume.</summary>
        Private Const MaxKeyBytes As Integer = 72

        ''' <summary>"OrpheanBeholderScryDoubt" -- the string bcrypt encrypts 64 times.</summary>
        Private Shared ReadOnly MagicCipher As Byte() = CryptoBytes.AsciiBytes("OrpheanBeholderScryDoubt")

        Private Sub New()
        End Sub

        ''' <summary>Encodes bytes with the bcrypt alphabet and no padding.</summary>
        Friend Shared Function EncodeDotSlash(data As Byte(), Optional maxBytes As Integer = -1) As String
            Dim take As Integer = If(maxBytes < 0, data.Length, Math.Min(maxBytes, data.Length))
            Dim sb As New StringBuilder()
            Dim i As Integer = 0
            While i < take
                Dim b0 As Integer = data(i)
                Dim b1 As Integer = If(i + 1 < take, data(i + 1), 0)
                Dim b2 As Integer = If(i + 2 < take, data(i + 2), 0)
                Dim n As Integer = (b0 << 16) Or (b1 << 8) Or b2
                Dim remaining As Integer = take - i
                Dim chars As Integer = If(remaining >= 3, 4, remaining + 1)
                For k As Integer = 0 To chars - 1
                    sb.Append(DotSlash((n >> (18 - 6 * k)) And &H3F))
                Next
                i += 3
            End While
            Return sb.ToString()
        End Function

        ''' <summary>Inverse of <see cref="EncodeDotSlash"/>.</summary>
        Friend Shared Function DecodeDotSlash(text As String) As Byte()
            Dim bytes As New List(Of Byte)()
            Dim bits As Integer = 0
            Dim nbits As Integer = 0
            For Each c As Char In text
                ' Go's decoder accepts the '=' padding that Proton appends to the 22 char salt.
                If c = "="c Then Continue For
                Dim v As Integer = DotSlash.IndexOf(c)
                If v < 0 Then Throw New FormatException("Invalid bcrypt base64 character: " & c)
                bits = (bits << 6) Or v
                nbits += 6
                If nbits >= 8 Then
                    nbits -= 8
                    bytes.Add(CByte((bits >> nbits) And &HFF))
                End If
            Next
            Return bytes.ToArray()
        End Function

        ''' <summary>
        ''' Proton's <c>bcrypt.HashBytes</c>. Parses "$2y$10$&lt;22 char salt&gt;" and
        ''' returns the full 60 character "$2y$10$&lt;salt&gt;&lt;31 char hash&gt;" string.
        ''' </summary>
        Friend Shared Function HashBytes(password As Byte(), saltString As String) As String
            Dim minor As Char = ChrW(0)
            Dim pos As Integer = 0

            If Not Consume(saltString, pos, "$"c) OrElse Not Consume(saltString, pos, "2"c) Then
                Throw New ArgumentException("Invalid bcrypt salt.", "saltString")
            End If
            If Not Consume(saltString, pos, "$"c) Then
                minor = saltString(pos)
                If minor <> "a"c AndAlso minor <> "y"c Then
                    Throw New ArgumentException("Invalid bcrypt minor version.", "saltString")
                End If
                pos += 1
                If Not Consume(saltString, pos, "$"c) Then
                    Throw New ArgumentException("Invalid bcrypt salt.", "saltString")
                End If
            End If

            If pos + 3 > saltString.Length Then
                Throw New ArgumentException("Invalid bcrypt salt.", "saltString")
            End If
            Dim roundsText As String = saltString.Substring(pos, 2)
            pos += 2
            If Not Consume(saltString, pos, "$"c) Then
                Throw New ArgumentException("Invalid bcrypt salt.", "saltString")
            End If
            Dim rounds As Integer = Integer.Parse(roundsText, CultureInfo.InvariantCulture)

            If pos + 22 > saltString.Length Then
                Throw New ArgumentException("Invalid bcrypt salt.", "saltString")
            End If
            Dim saltB64 As String = saltString.Substring(pos, 22)
            Dim saltBytes As Byte() = DecodeDotSlash(saltB64 & "==")
            If saltBytes.Length < 16 Then
                Throw New ArgumentException("Invalid bcrypt salt length.", "saltString")
            End If

            ' bcrypt's key schedule is defined over at most 72 bytes, and every
            ' reference implementation enforces that (python-bcrypt refuses a longer
            ' input outright). Hashing more would derive a hash no server can match.
            Dim take As Integer = Math.Min(password.Length, MaxKeyBytes)

            ' bcrypt terminates the key with a NUL byte.
            Dim key(take) As Byte
            Array.Copy(password, key, take)

            Dim raw As Byte() = CryptRaw(key, saltBytes, rounds)

            Dim sb As New StringBuilder(60)
            sb.Append("$2")
            If minor <> ChrW(0) Then sb.Append(minor)
            sb.Append("$"c)
            If rounds < 10 Then sb.Append("0"c)
            sb.Append(rounds.ToString(CultureInfo.InvariantCulture))
            sb.Append("$"c)
            sb.Append(saltB64)
            sb.Append(EncodeDotSlash(raw, HashBytesEncoded))
            Return sb.ToString()
        End Function

        Private Shared Function Consume(text As String, ByRef pos As Integer, expected As Char) As Boolean
            If pos < text.Length AndAlso text(pos) = expected Then
                pos += 1
                Return True
            End If
            Return False
        End Function

        ''' <summary>OpenBSD-style bcrypt: EksBlowfish key schedule then 64 ECB rounds.</summary>
        Private Shared Function CryptRaw(key As Byte(), salt As Byte(), cost As Integer) As Byte()
            Dim p(17) As UInteger
            Array.Copy(BlowfishTables.P0, p, 18)

            Dim s(3)() As UInteger
            Dim sources As UInteger()() = {BlowfishTables.S0, BlowfishTables.S1, BlowfishTables.S2, BlowfishTables.S3}
            For i As Integer = 0 To 3
                Dim box(255) As UInteger
                Array.Copy(sources(i), box, 256)
                s(i) = box
            Next

            ' Blowfish_expandstate(salt, key), then 2^cost rounds of expand0state.
            ExpandKey(p, s, key, salt)
            Dim iterations As Integer = 1 << cost
            For i As Integer = 1 To iterations
                ExpandKey(p, s, key, Nothing)
                ExpandKey(p, s, salt, Nothing)
            Next

            Dim cipher(5) As UInteger
            For i As Integer = 0 To 5
                cipher(i) = (CUInt(MagicCipher(i * 4)) << 24) Or (CUInt(MagicCipher(i * 4 + 1)) << 16) Or
                            (CUInt(MagicCipher(i * 4 + 2)) << 8) Or CUInt(MagicCipher(i * 4 + 3))
            Next

            For n As Integer = 1 To 64
                For i As Integer = 0 To 4 Step 2
                    Encipher(p, s, cipher(i), cipher(i + 1))
                Next
            Next

            Dim result(23) As Byte
            For i As Integer = 0 To 5
                result(i * 4) = CByte((cipher(i) >> 24) And &HFFUI)
                result(i * 4 + 1) = CByte((cipher(i) >> 16) And &HFFUI)
                result(i * 4 + 2) = CByte((cipher(i) >> 8) And &HFFUI)
                result(i * 4 + 3) = CByte(cipher(i) And &HFFUI)
            Next
            Return result
        End Function

        ''' <summary>
        ''' OpenBSD <c>Blowfish_expand0state</c> / <c>Blowfish_expandstate</c>. The salt
        ''' stream index is continuous across the P-array and all four S-boxes.
        ''' </summary>
        Private Shared Sub ExpandKey(p As UInteger(), s As UInteger()(), key As Byte(), data As Byte())
            For i As Integer = 0 To 17
                p(i) = p(i) Xor CyclicWord(key, i)
            Next

            Dim l As UInteger = 0
            Dim r As UInteger = 0
            Dim j As Integer = 0

            Dim idx As Integer = 0
            While idx < 18
                If data IsNot Nothing Then
                    l = l Xor Stream2Word(data, j)
                    r = r Xor Stream2Word(data, j)
                End If
                Encipher(p, s, l, r)
                p(idx) = l
                p(idx + 1) = r
                idx += 2
            End While

            For box As Integer = 0 To 3
                idx = 0
                While idx < 256
                    If data IsNot Nothing Then
                        l = l Xor Stream2Word(data, j)
                        r = r Xor Stream2Word(data, j)
                    End If
                    Encipher(p, s, l, r)
                    s(box)(idx) = l
                    s(box)(idx + 1) = r
                    idx += 2
                End While
            Next
        End Sub

        ''' <summary>OpenBSD <c>Blowfish_stream2word</c>: 4 big-endian bytes, index advances and wraps.</summary>
        Private Shared Function Stream2Word(data As Byte(), ByRef index As Integer) As UInteger
            Dim w As UInteger = 0
            For k As Integer = 0 To 3
                If index >= data.Length Then index = 0
                w = (w << 8) Or data(index)
                index += 1
            Next
            Return w
        End Function

        ''' <summary>32-bit big-endian word cycling through <paramref name="data"/>.</summary>
        Private Shared Function CyclicWord(data As Byte(), wordIndex As Integer) As UInteger
            Dim n As Integer = data.Length
            Dim w As UInteger = 0
            For k As Integer = 0 To 3
                w = (w << 8) Or data((wordIndex * 4 + k) Mod n)
            Next
            Return w
        End Function

        Private Shared Function F(s As UInteger()(), x As UInteger) As UInteger
            Dim y As UInteger = (s(0)(CInt((x >> 24) And &HFFUI)) + s(1)(CInt((x >> 16) And &HFFUI))) And WordMask
            y = y Xor s(2)(CInt((x >> 8) And &HFFUI))
            Return (y + s(3)(CInt(x And &HFFUI))) And WordMask
        End Function

        Private Shared Sub Encipher(p As UInteger(), s As UInteger()(), ByRef l As UInteger, ByRef r As UInteger)
            For i As Integer = 0 To 15
                l = l Xor p(i)
                r = r Xor F(s, l)
                Dim t As UInteger = l
                l = r
                r = t
            Next
            Dim t2 As UInteger = l
            l = r
            r = t2
            r = r Xor p(16)
            l = l Xor p(17)
        End Sub

    End Class
End Namespace
