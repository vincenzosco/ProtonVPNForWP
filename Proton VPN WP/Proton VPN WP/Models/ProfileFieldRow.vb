' One "label -> value" row of the setup instructions.
'
' Public with public members because the WinRT XAML binding engine only reflects
' over public types.
Namespace Models
    Public NotInheritable Class ProfileFieldRow
        Public Property Label As String
        Public Property Value As String
        Public Property IsSecret As Boolean
        Public Property Ordinal As Integer
    End Class
End Namespace
