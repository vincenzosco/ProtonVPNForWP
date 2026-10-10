' Minimal DER reader, enough for the X.509 structures this app validates.
'
' Only the definite-length, low-tag-number forms actually appear in Proton's
' certificate chain and their extensions, so those are what this supports; an
' indefinite length or a truncated buffer is rejected rather than guessed at.
' Every element keeps its raw TLV bytes, because a certificate signature is
' computed over the exact encoding of tbsCertificate.
Imports System.Collections.Generic

Namespace Crypto
    Friend NotInheritable Class Asn1

        Friend NotInheritable Class Element
            Friend Tag As Byte
            ''' <summary>The whole TLV (tag, length, content).</summary>
            Friend Raw As Byte()
            Friend Content As Byte()
            Friend Children As List(Of Element)

            Friend ReadOnly Property IsConstructed As Boolean
                Get
                    Return (CInt(Tag) And &H20) <> 0
                End Get
            End Property
        End Class

        Private Sub New()
        End Sub

        Friend Shared Function Parse(data As Byte()) As Element
            Dim offset As Integer = 0
            Return ParseElement(data, offset)
        End Function

        Friend Shared Function ParseElement(data As Byte(), ByRef offset As Integer) As Element
            If data Is Nothing OrElse offset >= data.Length Then Return Nothing

            Dim start As Integer = offset
            Dim tag As Byte = data(offset)
            offset += 1
            Dim length As Integer = ReadLength(data, offset)
            Dim contentStart As Integer = offset
            If contentStart + length > data.Length Then Throw New System.FormatException("DER element runs past the buffer.")

            Dim element As New Element()
            element.Tag = tag
            If length > 0 Then
                element.Content = New Byte(length - 1) {}
                Array.Copy(data, contentStart, element.Content, 0, length)
            Else
                element.Content = New Byte() {}
            End If
            element.Raw = New Byte(offset + length - start - 1) {}
            Array.Copy(data, start, element.Raw, 0, element.Raw.Length)
            offset = contentStart + length

            If element.IsConstructed Then
                element.Children = New List(Of Element)()
                Dim inner As Integer = 0
                While inner < element.Content.Length
                    Dim child As Element = ParseElement(element.Content, inner)
                    If child Is Nothing Then Exit While
                    element.Children.Add(child)
                End While
            End If
            Return element
        End Function

        Private Shared Function ReadLength(data As Byte(), ByRef offset As Integer) As Integer
            Dim first As Integer = CInt(data(offset))
            offset += 1
            If (first And &H80) = 0 Then Return first

            Dim count As Integer = first And &H7F
            If count = 0 Then Throw New System.FormatException("Indefinite DER lengths are not allowed.")

            Dim length As Integer = 0
            For i As Integer = 0 To count - 1
                length = (length << 8) Or CInt(data(offset))
                offset += 1
            Next
            Return length
        End Function

    End Class
End Namespace
