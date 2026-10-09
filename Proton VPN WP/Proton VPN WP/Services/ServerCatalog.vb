' The server catalogue.
'
' Because Proton closed public access to /vpn/logicals, the app ships a bundled
' snapshot and layers live data on top when the account API answers. That ordering
' is deliberate: the app must be usable with no network at all.
Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Models
Imports Windows.Data.Json
Imports Windows.Storage

Namespace Services

    Friend Enum CatalogueSource
        None = 0
        Bundled = 1
        Cache = 2
        Api = 3
    End Enum

    Friend NotInheritable Class ServerCatalog

        Private Const BundlePath As String = "Data/servers.json"
        Private Const CacheFileName As String = "catalogue-cache.json"

        Private ReadOnly _api As ProtonApiClient
        Private ReadOnly _settings As SettingsStore
        Private ReadOnly _servers As New List(Of ProtonLogical)()

        Friend Sub New(api As ProtonApiClient, settings As SettingsStore)
            _api = api
            _settings = settings
        End Sub

        Friend Property Source As CatalogueSource = CatalogueSource.None

        Friend ReadOnly Property Count As Integer
            Get
                Return _servers.Count
            End Get
        End Property

        ''' <summary>All loaded logical servers.</summary>
        Friend Function All() As IList(Of ProtonLogical)
            Return _servers
        End Function

        ''' <summary>
        ''' Loads servers: live API first when signed in, then a cached response, then
        ''' the bundled snapshot. Never returns an empty list while the bundle exists.
        ''' </summary>
        Friend Async Function LoadAsync(forceRefresh As Boolean) As Task(Of ApiResult(Of IList(Of ProtonLogical)))
            If Not forceRefresh AndAlso _servers.Count > 0 Then
                Return ApiResult(Of IList(Of ProtonLogical)).Success(_servers)
            End If

            Dim apiError As String = Nothing

            If _settings.Load().AutoRefreshCatalogue OrElse forceRefresh Then
                Dim live As ApiResult(Of JsonObject) = Await _api.GetLogicalsAsync()
                If live.Ok Then
                    Dim parsed As List(Of ProtonLogical) = ParseLogicals(live.Value)
                    If parsed.Count > 0 Then
                        Replace(parsed, CatalogueSource.Api)
                        Await WriteCacheAsync(live.Value.Stringify())
                        Dim updated As AppSettings = _settings.Load()
                        updated.LastCatalogueRefreshUtc = DateTime.UtcNow
                        _settings.Save(updated)
                        Return ApiResult(Of IList(Of ProtonLogical)).Success(_servers)
                    End If
                    apiError = "Proton returned an empty server list."
                Else
                    apiError = live.Message
                End If
            End If

            Dim cached As String = Await ReadCacheAsync()
            If Not String.IsNullOrEmpty(cached) Then
                Dim parsedCache As List(Of ProtonLogical) = ParseLogicals(Json.TryParseObject(cached))
                If parsedCache.Count > 0 Then
                    Replace(parsedCache, CatalogueSource.Cache)
                    Return ApiResult(Of IList(Of ProtonLogical)).Success(_servers, apiError)
                End If
            End If

            Return Await LoadBundledAsync(apiError)
        End Function

        Private Async Function LoadBundledAsync(apiError As String) As Task(Of ApiResult(Of IList(Of ProtonLogical)))
            Try
                Dim file As StorageFile = Await StorageFile.GetFileFromApplicationUriAsync(New Uri("ms-appx:///" & BundlePath))
                Dim text As String = Await FileIO.ReadTextAsync(file)
                Dim parsed As List(Of ProtonLogical) = ParseLogicals(Json.TryParseObject(text))
                If parsed.Count = 0 Then
                    Return ApiResult(Of IList(Of ProtonLogical)).Failure("The bundled server list is empty or unreadable.")
                End If
                Replace(parsed, CatalogueSource.Bundled)
                Dim message As String = "Using the offline server list."
                If Not String.IsNullOrEmpty(apiError) Then message &= " " & apiError
                Return ApiResult(Of IList(Of ProtonLogical)).Success(_servers, message)
            Catch ex As Exception
                Log.Warn("Bundled catalogue unavailable: " & ex.Message)
                Return ApiResult(Of IList(Of ProtonLogical)).Failure("No server list is available on this device.")
            End Try
        End Function

        Private Sub Replace(servers As List(Of ProtonLogical), source As CatalogueSource)
            _servers.Clear()
            _servers.AddRange(servers)
            Me.Source = source
            Log.Info("Catalogue loaded: " & _servers.Count.ToString() & " logical servers from " & Source.ToString())
        End Sub

        ''' <summary>
        ''' Parses either a full /vpn/logicals envelope ({"LogicalServers":[...]}) or a
        ''' bare array, and ignores entries it cannot read.
        ''' </summary>
        Friend Shared Function ParseLogicals(root As JsonObject) As List(Of ProtonLogical)
            Dim result As New List(Of ProtonLogical)()
            If root Is Nothing Then Return result

            Dim array As JsonArray = Json.TryArray(root, "LogicalServers")
            If array Is Nothing Then array = Json.TryArray(root, "Servers")
            If array Is Nothing Then Return result

            For Each item As IJsonValue In array
                If item IsNot Nothing AndAlso item.ValueType = JsonValueType.Object Then
                    Dim logical As ProtonLogical = ProtonLogical.FromJson(item.GetObject())
                    If logical IsNot Nothing AndAlso Not String.IsNullOrEmpty(logical.Name) Then
                        result.Add(logical)
                    End If
                End If
            Next
            Return result
        End Function

        ''' <summary>Free-text search plus the country and feature filters.</summary>
        Friend Function Search(query As String, countryCode As String, onlyFree As Boolean, onlyAvailable As Boolean) As List(Of ProtonLogical)
            Dim results As New List(Of ProtonLogical)()
            For Each server As ProtonLogical In _servers
                If onlyFree AndAlso server.Tier <> ServerTier.Free Then Continue For
                If onlyAvailable AndAlso Not server.IsAvailable Then Continue For
                If Not String.IsNullOrEmpty(countryCode) AndAlso
                   Not String.Equals(server.CountryCode, countryCode, StringComparison.OrdinalIgnoreCase) Then Continue For

                If Not String.IsNullOrEmpty(query) Then
                    Dim haystack As String = (server.Name & " " & server.City & " " & server.Country & " " &
                                              server.CountryCode & " " & server.FeatureText).ToLowerInvariant()
                    If Not haystack.Contains(query.Trim().ToLowerInvariant()) Then Continue For
                End If
                results.Add(server)
            Next
            Return results
        End Function

        ''' <summary>The distinct countries present, ordered by display name.</summary>
        Friend Function Countries() As List(Of KeyValuePair(Of String, String))
            Dim map As New Dictionary(Of String, String)()
            For Each server As ProtonLogical In _servers
                Dim code As String = If(server.CountryCode, "")
                Dim name As String = If(server.Country, code)
                If code.Length = 0 Then Continue For
                If Not map.ContainsKey(code) Then map.Add(code, name)
            Next

            Dim list As New List(Of KeyValuePair(Of String, String))()
            For Each pair As KeyValuePair(Of String, String) In map
                list.Add(pair)
            Next
            list.Sort(Function(a, b) String.Compare(a.Value, b.Value, StringComparison.OrdinalIgnoreCase))
            Return list
        End Function

        Friend Function FindByName(name As String) As ProtonLogical
            If String.IsNullOrEmpty(name) Then Return Nothing
            For Each server As ProtonLogical In _servers
                If String.Equals(server.Name, name, StringComparison.OrdinalIgnoreCase) Then Return server
            Next
            Return Nothing
        End Function

        ''' <summary>Load-balanced pick: available first, then the least loaded.</summary>
        Friend Function FindFastestInCountry(countryCode As String, onlyFree As Boolean) As ProtonLogical
            Dim best As ProtonLogical = Nothing
            For Each server As ProtonLogical In _servers
                If Not server.IsAvailable Then Continue For
                If Not String.IsNullOrEmpty(countryCode) AndAlso
                   Not String.Equals(server.CountryCode, countryCode, StringComparison.OrdinalIgnoreCase) Then Continue For
                If onlyFree AndAlso server.Tier <> ServerTier.Free Then Continue For
                If best Is Nothing OrElse server.Load < best.Load Then best = server
            Next
            Return best
        End Function

        ''' <summary>
        ''' Drops the cached response and the in-memory list, so the next load has to go
        ''' back to the API or the bundled snapshot.
        ''' </summary>
        Friend Async Function ResetAsync() As Task
            _servers.Clear()
            Source = CatalogueSource.None
            Try
                Dim folder As StorageFolder = Windows.Storage.ApplicationData.Current.LocalFolder
                Dim file As StorageFile = Await TryGetCacheFileAsync(folder)
                If file IsNot Nothing Then Await file.DeleteAsync()
            Catch ex As Exception
                Log.Warn("Could not delete the catalogue cache: " & ex.Message)
            End Try
        End Function

        ''' <summary>
        ''' WP8.1's StorageFolder has no TryGetItemAsync, so a missing file is reported
        ''' as an exception and turned back into Nothing here.
        ''' </summary>
        Private Shared Async Function TryGetCacheFileAsync(folder As StorageFolder) As Task(Of StorageFile)
            Try
                Return Await folder.GetFileAsync(CacheFileName)
            Catch ex As Exception
                Return Nothing
            End Try
        End Function

        Private Shared Async Function ReadCacheAsync() As Task(Of String)
            Try
                Dim folder As StorageFolder = Windows.Storage.ApplicationData.Current.LocalFolder
                Dim file As StorageFile = Await TryGetCacheFileAsync(folder)
                If file Is Nothing Then Return Nothing
                Return Await FileIO.ReadTextAsync(file)
            Catch ex As Exception
                Log.Warn("Could not read the catalogue cache: " & ex.Message)
                Return Nothing
            End Try
        End Function

        Private Shared Async Function WriteCacheAsync(text As String) As Task
            Try
                Dim folder As StorageFolder = Windows.Storage.ApplicationData.Current.LocalFolder
                Dim file As StorageFile = Await folder.CreateFileAsync(CacheFileName, CreationCollisionOption.ReplaceExisting)
                Await FileIO.WriteTextAsync(file, text)
            Catch ex As Exception
                Log.Warn("Could not write the catalogue cache: " & ex.Message)
            End Try
        End Function

    End Class
End Namespace
