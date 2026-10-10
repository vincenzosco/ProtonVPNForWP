' HTTP access to Proton's account and VPN APIs.
'
' TLS: these requests run over the app's own TLS 1.3 client (Tls.TlsHttpClient),
' not over Windows.Web.Http. They have to: Windows Phone 8.1's Schannel offers no
' cipher suite Proton's edge accepts, so the platform's stack is refused before a
' byte is sent (see the README and the ledger under .superpowers/sdd). The client
' authenticates the server before any application byte is trusted -- the
' CertificateVerify signature is checked with the leaf key and the chain is
' verified against the pinned ISRG roots -- and it has no mode that skips that.
'
' The endpoint names and the x-pm-appversion header are the parts most likely to
' drift as Proton evolves their API; every call returns an ApiResult instead of
' throwing so the UI can fall back to the bundled catalogue.
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Models
Imports Windows.Data.Json

' Tls.* is referenced fully qualified rather than through an Imports clause: the
' shipping project's RootNamespace makes `Imports Tls` resolve nowhere, and the
' desktop harness compiles the same Tls sources without a root namespace.

Namespace Services
    Friend NotInheritable Class ProtonApiClient

        ''' <summary>Account (authentication) API root.</summary>
        Friend Const AuthHost As String = "https://account.proton.me/api"
        ''' <summary>VPN API root. The legacy api.protonvpn.ch host was retired in 2025.</summary>
        Friend Const VpnHost As String = "https://vpn-api.proton.me"

        ''' <summary>
        ''' The app identifier Proton's API accepts. It is not cosmetic: the edge
        ''' rejects an unknown product with 400 Code 5002 ("Invalid app version")
        ''' before it looks at the request at all, and rejects a product whose
        ''' version it no longer supports with 422 Code 5003. `windows-vpn@5.0.0.0`
        ''' is the lowest accepted version of the VPN product as measured on
        ''' 2026-10-10; the value this app shipped with, `web-vpn@5.0.0`, is not a
        ''' product Proton's account API knows.
        ''' </summary>
        Private Const AppVersion As String = "windows-vpn@5.0.0.0"

        ''' <summary>Seconds after which a request that has not answered is abandoned.</summary>
        Private Const RequestTimeoutSeconds As Integer = 30

        Private Const HttpsPort As Integer = 443

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

        ''' <summary>
        ''' Completes an in-flight two-factor sign-in. It needs the UID that /auth
        ''' returned alongside code 1001, sent as x-pm-uid -- without it the request
        ''' does not identify the attempt it is continuing.
        ''' </summary>
        Friend Async Function PostTwoFactorAsync(uid As String, code As String) As Task(Of ApiResult(Of JsonObject))
            Dim body As New JsonObject()
            body.SetNamedValue("TwoFactorCode", JsonValue.CreateStringValue(code))
            Return Await PostJsonAsync(AuthHost & "/auth/2fa", body, uid, Nothing)
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
            Return Await SendAsync("POST", url, body.Stringify(), uid, token)
        End Function

        Private Async Function GetJsonAsync(url As String) As Task(Of ApiResult(Of JsonObject))
            Return Await SendAsync("GET", url, Nothing, UserId, AccessToken)
        End Function

        ' The body parameter must NOT be called "json": VB is case-insensitive, so a
        ' local named json would shadow the Json helper class used below.
        Private Async Function SendAsync(method As String, url As String, jsonBody As String,
                                         uid As String, token As String) As Task(Of ApiResult(Of JsonObject))
            Dim effectiveUid As String = If(uid, UserId)
            Dim effectiveToken As String = If(token, AccessToken)
            Dim transport As Tls.SocketTlsTransport = Nothing

            Try
                Dim headers As New System.Collections.Generic.Dictionary(Of String, String)()
                headers("Accept") = "application/vnd.protonmail.v1+json"
                headers("x-pm-appversion") = AppVersion
                If Not String.IsNullOrEmpty(effectiveUid) Then headers("x-pm-uid") = effectiveUid
                If Not String.IsNullOrEmpty(effectiveToken) Then headers("Authorization") = "Bearer " & effectiveToken

                transport = Await Tls.SocketTlsTransport.ConnectAsync(New Uri(url).Host, HttpsPort)

                Dim sent As Task(Of Tls.HttpReply)
                If method = "GET" Then
                    sent = Tls.TlsHttpClient.GetAsync(transport, New SystemRandomSource(), url, headers)
                Else
                    sent = Tls.TlsHttpClient.PostAsync(transport, New SystemRandomSource(), url, jsonBody, headers)
                End If

                ' The request as a whole is bounded. A connection that opens and then
                ' goes quiet would otherwise leave the caller pending forever: closing
                ' the socket is what aborts the stalled read.
                Dim finished As Task = Await Task.WhenAny(sent, Task.Delay(TimeSpan.FromSeconds(RequestTimeoutSeconds)))
                If Not Object.ReferenceEquals(finished, sent) Then
                    transport.Close()
                    Return ApiResult(Of JsonObject).Failure("The request to Proton timed out.")
                End If

                Dim reply As Tls.HttpReply = Await sent
                Log.Info("HTTP " & reply.StatusCode.ToString() & " " & method & " " & SafePath(url))

                Dim text As String = reply.Body
                If String.IsNullOrEmpty(text) Then
                    Return ApiResult(Of JsonObject).Failure("Empty response from the server (" & reply.StatusCode.ToString() & ").")
                End If

                Dim parsed As JsonObject = Json.TryParseObject(text)
                If parsed Is Nothing Then
                    Return ApiResult(Of JsonObject).Failure("The server returned a response the app could not read.")
                End If

                Dim code As Integer = Json.TryInt(parsed, "Code", 0)
                If code = 1000 Then Return ApiResult(Of JsonObject).Success(parsed)

                ' The code travels with the refusal, because it is the only thing that
                ' says which refusal this is; the message below is for the user, and
                ' a caller branching on it would be reading a protocol value out of
                ' prose.
                Dim errorText As String = Json.TryString(parsed, "Error")
                If String.IsNullOrEmpty(errorText) Then errorText = DescribeCode(code)
                Return ApiResult(Of JsonObject).Failure(errorText & " (code " & code.ToString() & ")", code)
            Catch ex As Exception
                Log.Warn("Network request failed: " & ex.GetType().Name & " " & ex.Message)
                ' Log-only and deliberately not awaited: the caller has an error to show
                ' and must not wait ~15s for a diagnosis. It runs at most once per 20s,
                ' and it is what turns a bare transport failure into the layer that refused.
                ConnectivityService.DiagnoseAsync("the request to " & SafePath(url) & " did not reach the host", url)
                Return ApiResult(Of JsonObject).Failure(ReachabilityFailureMessage())
            Finally
                If transport IsNot Nothing Then transport.Close()
            End Try
        End Function

        ''' <summary>
        ''' The message to show when a request did not reach Proton.
        '''
        ''' "Check the connection and try again" blamed the user's network for what was,
        ''' on this platform, the platform's own limit -- and now that the app carries
        ''' its own TLS 1.3 client, the platform is no longer the reason a request can
        ''' fail. The connection profile is still the one signal available without
        ''' waiting, and it separates the two answers worth telling apart: no profile
        ''' means the device really is offline; a profile with internet access means the
        ''' request was refused somewhere between here and Proton -- the handshake (an
        ''' untrusted certificate, an alert), DNS, or Proton itself -- and the offline
        ''' server list still works either way.
        ''' </summary>
        Private Shared Function ReachabilityFailureMessage() As String
            If Not ConnectivityService.HasInternetAccess() Then
                Return "You appear to be offline. Connect to a network and try again."
            End If
            Return "This device has a connection, but the request to Proton did not complete: " &
                   "the secure handshake or the network refused it. The offline server list still works."
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

        ''' <summary>
        ''' The fixed sentence for one of Proton's codes.
        '''
        ''' Shared rather than private because the sign-in path logs it: the log line
        ''' names the code, and never Proton's own `Error` text, which quotes the
        ''' account identifiers back into a file the user can share.
        ''' </summary>
        Friend Shared Function DescribeCode(code As Integer) As String
            Select Case code
                ' 2028 is what a real account answers with while Proton has limited it;
                ' the server's own Error text carries the explanation and the appeal
                ' path, and the user-facing message prefers it, but a refusal with no
                ' Error text must not collapse to "Proton rejected the request."
                Case 2028 : Return "Proton has temporarily limited this account."
                Case 8002 : Return "Incorrect login credentials."
                Case 1001 : Return "Two-factor authentication is required."
                Case 1002 : Return "Two-factor authentication is required."
                Case 5002 : Return "Proton rejected this app's version."
                Case 5003 : Return "Proton no longer supports this app's version."
                Case 12087 : Return "Too many attempts. Try again later."
                Case 2001 : Return "The account is not valid."
                Case Else : Return "Proton rejected the request."
            End Select
        End Function

    End Class
End Namespace
