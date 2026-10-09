' Small byte helpers shared by the crypto code. Kept dependency-free so the same
' source compiles into the WP8.1 app and into the desktop verification harness.
Namespace Crypto
    Friend NotInheritable Class CryptoBytes

        Private Sub New()
        End Sub

        Friend Shared Function Concat(ParamArray parts As Byte()()) As Byte()
            Dim total As Integer = 0
            For Each part As Byte() In parts
                If part IsNot Nothing Then total += part.Length
            Next
            Dim result(total - 1) As Byte
            Dim offset As Integer = 0
            For Each part As Byte() In parts
                If part IsNot Nothing Then
                    Array.Copy(part, 0, result, offset, part.Length)
                    offset += part.Length
                End If
            Next
            Return result
        End Function

        Friend Shared Function ToHex(data As Byte()) As String
            If data Is Nothing Then Return String.Empty
            Dim sb As New System.Text.StringBuilder(data.Length * 2)
            For Each b As Byte In data
                sb.Append(b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture))
            Next
            Return sb.ToString()
        End Function

        Friend Shared Function FromHex(hex As String) As Byte()
            If hex Is Nothing Then Return New Byte() {}
            Dim clean As String = hex.Trim()
            If clean.Length Mod 2 <> 0 Then Throw New FormatException("Hex string must have an even length.")
            Dim result(clean.Length \ 2 - 1) As Byte
            For i As Integer = 0 To result.Length - 1
                result(i) = Byte.Parse(clean.Substring(i * 2, 2), System.Globalization.NumberStyles.HexNumber,
                                       System.Globalization.CultureInfo.InvariantCulture)
            Next
            Return result
        End Function

        Friend Shared Function ConstantTimeEquals(a As Byte(), b As Byte()) As Boolean
            If a Is Nothing OrElse b Is Nothing OrElse a.Length <> b.Length Then Return False
            Dim diff As Integer = 0
            For i As Integer = 0 To a.Length - 1
                diff = diff Or (CInt(a(i)) Xor CInt(b(i)))
            Next
            Return diff = 0
        End Function

    End Class
End Namespace
