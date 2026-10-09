' Defensive wrappers over Windows.Data.Json.
'
' The Proton API and the bundled catalogue are both attacker-influenced input as
' far as the app is concerned, so every accessor tolerates a missing key, a null
' value and a wrong JSON type instead of throwing (see the plan's Review Focus).
Imports Windows.Data.Json

Namespace Services
    Friend NotInheritable Class Json

        Private Sub New()
        End Sub

        Friend Shared Function TryParseObject(text As String) As JsonObject
            If String.IsNullOrEmpty(text) Then Return Nothing
            Try
                Return JsonObject.Parse(text)
            Catch ex As Exception
                Log.Warn("JSON parse failed: " & ex.Message)
                Return Nothing
            End Try
        End Function

        Friend Shared Function TryArray(obj As JsonObject, name As String) As JsonArray
            If obj Is Nothing Then Return Nothing
            Try
                Dim value As IJsonValue = obj.GetNamedValue(name)
                If value Is Nothing OrElse value.ValueType <> JsonValueType.Array Then Return Nothing
                Return value.GetArray()
            Catch
                Return Nothing
            End Try
        End Function

        Friend Shared Function TryObject(obj As JsonObject, name As String) As JsonObject
            If obj Is Nothing Then Return Nothing
            Try
                Dim value As IJsonValue = obj.GetNamedValue(name)
                If value Is Nothing OrElse value.ValueType <> JsonValueType.Object Then Return Nothing
                Return value.GetObject()
            Catch
                Return Nothing
            End Try
        End Function

        Friend Shared Function TryString(obj As JsonObject, name As String) As String
            If obj Is Nothing Then Return Nothing
            Try
                Dim value As IJsonValue = obj.GetNamedValue(name)
                If value Is Nothing OrElse value.ValueType <> JsonValueType.String Then Return Nothing
                Return value.GetString()
            Catch
                Return Nothing
            End Try
        End Function

        Friend Shared Function TryBool(obj As JsonObject, name As String, fallback As Boolean) As Boolean
            If obj Is Nothing Then Return fallback
            Try
                Dim value As IJsonValue = obj.GetNamedValue(name)
                If value Is Nothing OrElse value.ValueType <> JsonValueType.Boolean Then Return fallback
                Return value.GetBoolean()
            Catch
                Return fallback
            End Try
        End Function

        Friend Shared Function TryNumber(obj As JsonObject, name As String, fallback As Double) As Double
            If obj Is Nothing Then Return fallback
            Try
                Dim value As IJsonValue = obj.GetNamedValue(name)
                If value Is Nothing OrElse value.ValueType <> JsonValueType.Number Then Return fallback
                Return value.GetNumber()
            Catch
                Return fallback
            End Try
        End Function

        ''' <summary>
        ''' Integer accessor that keeps this class's "never throws" contract.
        '''
        ''' TryNumber catches, but the narrowing conversion used to sit outside any
        ''' catch: a JSON number outside Integer range raised OverflowException, which
        ''' escaped ServerCatalog.LoadAsync and reached an unhandled-exception handler.
        ''' </summary>
        Friend Shared Function TryInt(obj As JsonObject, name As String, fallback As Integer) As Integer
            Dim number As Double = TryNumber(obj, name, CDbl(fallback))
            If Double.IsNaN(number) OrElse Double.IsInfinity(number) Then Return fallback
            If number > CDbl(Integer.MaxValue) OrElse number < CDbl(Integer.MinValue) Then Return fallback
            Return CInt(Math.Round(number))
        End Function

        Friend Shared Function Str(value As String) As String
            If value Is Nothing Then Return Nothing
            Return TryCast(value, String)
        End Function

    End Class
End Namespace
