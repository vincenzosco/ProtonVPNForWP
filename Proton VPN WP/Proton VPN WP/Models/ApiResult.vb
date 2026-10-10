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

        ''' <summary>
        ''' The `Code` Proton answered with, or 0 when no body carrying one was read.
        '''
        ''' It belongs on the result because the caller has to branch on it: 1000 is
        ''' success, 1001/1002 is a second factor rather than a failure, 2028 is a
        ''' limited account, 8002 is a refused proof. Reading those back out of
        ''' `Message` is what lost the payload the second factor continues with.
        ''' </summary>
        Friend Property Code As Integer

        Protected Sub New(ok As Boolean, message As String, Optional code As Integer = 0)
            Me.Ok = ok
            Me.Message = message
            Me.Code = code
        End Sub

        Friend Shared Function Success(Optional message As String = Nothing) As ApiResult
            Return New ApiResult(True, message)
        End Function

        Friend Shared Function Failure(message As String, Optional code As Integer = 0) As ApiResult
            Return New ApiResult(False, message, code)
        End Function

    End Class

    Friend NotInheritable Class ApiResult(Of T)
        Inherits ApiResult

        Friend Property Value As T

        Private Sub New(ok As Boolean, message As String, value As T, Optional code As Integer = 0)
            MyBase.New(ok, message, code)
            Me.Value = value
        End Sub

        Friend Shared Shadows Function Success(value As T, Optional message As String = Nothing) As ApiResult(Of T)
            Return New ApiResult(Of T)(True, message, value)
        End Function

        ' The generic factory Shadows the one on ApiResult, so it needs the code
        ' parameter too: a caller holding an ApiResult(Of JsonObject) resolves here,
        ' and without it the two-argument call is BC30057.
        Friend Shared Shadows Function Failure(message As String, Optional code As Integer = 0) As ApiResult(Of T)
            Return New ApiResult(Of T)(False, message, Nothing, code)
        End Function

        ''' <summary>
        ''' A refusal that still carries the body it refused on.
        '''
        ''' 1001/1002 is not only a refusal: it carries the `UID` and the server proof
        ''' that identify the exchange a typed second-factor code continues, and
        ''' `Ok = False` used to mean those were thrown away with the body.
        ''' </summary>
        Friend Shared Shadows Function Failure(message As String, value As T, code As Integer) As ApiResult(Of T)
            Return New ApiResult(Of T)(False, message, value, code)
        End Function

    End Class
End Namespace
