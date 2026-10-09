' A tiny result type. Network calls fail for ordinary reasons (offline, API
' changes, 429s); modelling that as a value keeps the view models free of
' control flow built on exceptions.
'
' NOTE: this must stay VB 12 (VS2013) compatible, so no auto-implemented read-only
' properties and no NotInheritable base class.
Namespace Models

    Friend Class ApiResult

        Friend Property Ok As Boolean
        Friend Property Message As String

        Protected Sub New(ok As Boolean, message As String)
            Me.Ok = ok
            Me.Message = message
        End Sub

        Friend Shared Function Success(Optional message As String = Nothing) As ApiResult
            Return New ApiResult(True, message)
        End Function

        Friend Shared Function Failure(message As String) As ApiResult
            Return New ApiResult(False, message)
        End Function

    End Class

    Friend NotInheritable Class ApiResult(Of T)
        Inherits ApiResult

        Friend Property Value As T

        Private Sub New(ok As Boolean, message As String, value As T)
            MyBase.New(ok, message)
            Me.Value = value
        End Sub

        Friend Shared Function Success(value As T, Optional message As String = Nothing) As ApiResult(Of T)
            Return New ApiResult(Of T)(True, message, value)
        End Function

        Friend Shared Shadows Function Failure(message As String) As ApiResult(Of T)
            Return New ApiResult(Of T)(False, message, Nothing)
        End Function

    End Class
End Namespace
