' HTTP access to Proton's account and VPN APIs.
'
' TLS: Windows.Web.Http validates server certificates by default and this client
' never installs a handler that relaxes that. There is exactly one place where
' certificates would be touched and it does not exist here, on purpose.
'
' The endpoint names and the x-pm-appversion header are the parts most likely to
' drift as Proton evolves their API; every call returns an ApiResult instead of
' throwing so the UI can fall back to the bundled catalogue.
Imports System.Threading
Imports System.Threading.Tasks
Imports Models
Imports Windows.Data.Json
Imports Windows.Storage.Streams
Imports Windows.Web.Http

Namespace Services
    Friend NotInheritable Class ProtonApiClient
        Implements IDisposable

        ''' <summary>Account (authentication) API root.</summary>
        Friend Const AuthHost As String = "https://account.proton.me/api"
        ''' <summary>VPN API root. The legacy api.protonvpn.ch host was retired in 2025.</summary>
        Friend Const VpnHost As String = "https://vpn-api.proton.me"

        Private Const AppVersion As String = "web-vpn@5.0.0"
        Private Const RequestTimeout As Integer = 30

        Private ReadOnly _http As New HttpClient()

        ''' <summary>UID of the currently signed-in account, sent as x-pm-uid.</summary>
        Friend Property UserId As String

        ''' <summary>Bearer token of the currently signed-in account.</summary>
        Friend Property AccessToken As String

        Friend Async Function GetAuthInfoAsync(username As String) As Task(Of ApiResult(Of JsonObject))
            Dim body As New JsonObject()
            body.SetNamedValue("Username", JsonValue.CreateStringValue(username))
            Return Await PostJsonAsync(AuthHost & "/auth/info", body)
        End Function

        Friend Async Function PostAuthAsync(username As String, clientEphemeral As String,
                                            clientProof As String, srpSession As String) As Task(Of ApiResult(Of JsonObject))
            Dim body As New JsonObject()
            body.SetNamedValue("Username", JsonValue.CreateStringValue(username))
            body.SetNamedValue("ClientEphemeral", JsonValue.CreateStringValue(clientEphemeral))
            body.SetNamedValue("ClientProof", JsonValue.CreateStringValue(clientProof))
            body.SetNamedValue("SRPSession", JsonValue.CreateStringValue(srpSession))
            Return Await PostJsonAsync(AuthHost & "/auth", body)
        End Function

        Friend Async Function PostTwoFactorAsync(username As String, code As String) As Task(Of ApiResult(Of JsonObject))
            Dim body As New JsonObject()
            body.SetNamedValue("TwoFactorCode", JsonValue.CreateStringValue(code))
            Return Await PostJsonAsync(AuthHost & "/auth/2fa", body)
        End Function

        Friend Async Function RefreshAsync(uid As String, refreshToken As String) As Task(Of ApiResult(Of JsonObject))
            Dim body As New JsonObject()
            body.SetNamedValue("ResponseType", JsonValue.CreateStringValue("token"))
            body.SetNamedValue("GrantType", JsonValue.CreateStringValue("refresh_token"))
            body.SetNamedValue("RefreshToken", JsonValue.CreateStringValue(refreshToken))
            body.SetNamedValue("RedirectURI", JsonValue.CreateStringValue("https://protonvpn.com"))
            Return Await PostJsonAsync(AuthHost & "/auth/refresh", body, uid, Nothing)
        End Function

        Friend Async Function GetLogicalsAsync() As Task(Of ApiResult(Of JsonObject))
            Return Await GetJsonAsync(VpnHost & "/vpn/logicals")
        End Function

        Friend Async Function GetVpnCredentialsAsync() As Task(Of ApiResult(Of JsonObject))
            Return Await GetJsonAsync(VpnHost & "/vpn/credentials")
        End Function

        ''' <summary>Returns Code 1000 on success and the parsed payload.</summary>
        Private Async Function PostJsonAsync(url As String, body As JsonObject,
                                             Optional uid As String = Nothing,
                                             Optional token As String = Nothing) As Task(Of ApiResult(Of JsonObject))
            Return Await SendAsync(HttpMethod.Post, url, body.Stringify(), uid, token)
        End Function

        Private Async Function GetJsonAsync(url As String) As Task(Of ApiResult(Of JsonObject))
            Return Await SendAsync(HttpMethod.Get, url, Nothing, UserId, AccessToken)
        End Function

        Private Async Function SendAsync(method As HttpMethod, url As String, json As String,
                                         uid As String, token As String) As Task(Of ApiResult(Of JsonObject))
            Dim effectiveUid As String = If(uid, UserId)
            Dim effectiveToken As String = If(token, AccessToken)

            Try
                Using request As New HttpRequestMessage(method, New Uri(url))
                    request.Headers.Add("x-pm-appversion", AppVersion)
                    request.Headers.Add("Accept", "application/vnd.protonmail.v1+json")
                    If Not String.IsNullOrEmpty(effectiveUid) Then request.Headers.Add("x-pm-uid", effectiveUid)
                    If Not String.IsNullOrEmpty(effectiveToken) Then
                        request.Headers.Authorization = New Windows.Web.Http.Headers.HttpCredentialsHeaderValue("Bearer", effectiveToken)
                    End If
                    If json IsNot Nothing Then
                        request.Content = New HttpStringContent(json, UnicodeEncoding.Utf8, "application/json")
                    End If

                    Using cancellation As New CancellationTokenSource(TimeSpan.FromSeconds(RequestTimeout))
                        Dim response As HttpResponseMessage = Await _http.SendRequestAsync(request).AsTask(cancellation.Token)
                        Dim text As String = Await response.Content.ReadAsStringAsync().AsTask(cancellation.Token)

                        Log.Info("HTTP " & CInt(response.StatusCode) & " " & method.ToString() & " " & SafePath(url))

                        If String.IsNullOrEmpty(text) Then
                            Return ApiResult(Of JsonObject).Failure("Empty response from the server (" & CInt(response.StatusCode) & ").")
                        End If

                        Dim parsed As JsonObject = Json.TryParseObject(text)
                        If parsed Is Nothing Then
                            Return ApiResult(Of JsonObject).Failure("The server returned a response the app could not read.")
                        End If

                        Dim code As Integer = Json.TryInt(parsed, "Code", 0)
                        If code = 1000 Then Return ApiResult(Of JsonObject).Success(parsed)

                        Dim errorText As String = Json.TryString(parsed, "Error")
                        If String.IsNullOrEmpty(errorText) Then errorText = DescribeCode(code)
                        Return ApiResult(Of JsonObject).Failure(errorText & " (code " & code.ToString() & ")")
                    End Using
                End Using
            Catch ex As TaskCanceledException
                Return ApiResult(Of JsonObject).Failure("The request to Proton timed out.")
            Catch ex As Exception
                Log.Warn("Network request failed: " & ex.GetType().Name & " " & ex.Message)
                Return ApiResult(Of JsonObject).Failure("Could not reach Proton. Check the connection and try again.")
            End Try
        End Function

        ''' <summary>Logs the endpoint without the query string, which can carry tokens.</summary>
        Private Shared Function SafePath(url As String) As String
            Try
                Dim uri As New Uri(url)
                Return uri.Host & uri.AbsolutePath
            Catch
                Return "(unparsable url)"
            End Try
        End Function

        Private Shared Function DescribeCode(code As Integer) As String
            Select Case code
                Case 8002 : Return "Incorrect login credentials."
                Case 1001 : Return "Two-factor authentication is required."
                Case 1002 : Return "Two-factor authentication is required."
                Case 12087 : Return "Too many attempts. Try again later."
                Case 2001 : Return "The account is not valid."
                Case Else : Return "Proton rejected the request."
            End Select
        End Function

        Friend Sub Dispose() Implements IDisposable.Dispose
            _http.Dispose()
        End Sub

    End Class
End Namespace
