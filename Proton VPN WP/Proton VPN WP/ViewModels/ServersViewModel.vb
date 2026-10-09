' Server browser view model: filtering, grouping and the free-tier switch.
Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Models
Imports Proton_VPN_WP.Services

Namespace ViewModels
    Friend NotInheritable Class ServersViewModel
        Inherits ViewModelBase

        Private ReadOnly _all As New List(Of ProtonLogical)()
        Private _searchText As String = String.Empty
        Private _onlyFree As Boolean
        Private _summary As String = "—"

        ' VB12 has no read-only auto-properties, so the collection is a backing field
        ' exposed through an explicit getter.
        Private ReadOnly _visible As New ObservableCollection(Of ProtonLogical)()

        Friend ReadOnly Property Servers As ObservableCollection(Of ProtonLogical)
            Get
                Return _visible
            End Get
        End Property

        Friend Property SearchText As String
            Get
                Return _searchText
            End Get
            Set(value As String)
                If SetProperty(_searchText, value, "SearchText") Then ApplyFilter()
            End Set
        End Property

        Friend Property OnlyFree As Boolean
            Get
                Return _onlyFree
            End Get
            Set(value As Boolean)
                If SetProperty(_onlyFree, value, "OnlyFree") Then
                    ' The switch is a user preference, so it outlives the page.
                    Dim settings As AppSettings = AppServices.Current.Settings.Load()
                    settings.OnlyFreeServers = value
                    AppServices.Current.Settings.Save(settings)
                    ApplyFilter()
                End If
            End Set
        End Property

        Friend Property Summary As String
            Get
                Return _summary
            End Get
            Set(value As String)
                SetProperty(_summary, value, "Summary")
            End Set
        End Property

        Friend Async Function LoadAsync(forceRefresh As Boolean) As Task(Of ApiResult(Of IList(Of ProtonLogical)))
            IsBusy = True
            StatusMessage = "Loading servers…"
            Try
                Dim settings As AppSettings = AppServices.Current.Settings.Load()
                ' Set the backing field so loading the stored preference does not
                ' immediately write it back to disk.
                _onlyFree = settings.OnlyFreeServers
                RaisePropertyChanged("OnlyFree")

                Dim result As ApiResult(Of IList(Of ProtonLogical)) = Await AppServices.Current.Catalog.LoadAsync(forceRefresh)
                _all.Clear()
                If result.Value IsNot Nothing Then _all.AddRange(result.Value)

                ApplyFilter()
                StatusMessage = result.Message
                Return result
            Finally
                IsBusy = False
            End Try
        End Function

        Friend Sub ApplyFilter()
            Dim query As String = If(_searchText, String.Empty).Trim()
            Servers.Clear()

            For Each server In _all
                If _onlyFree AndAlso server.Tier <> ServerTier.Free Then Continue For
                If query.Length > 0 AndAlso Not Matches(server, query) Then Continue For
                Servers.Add(server)
            Next

            Summary = Servers.Count.ToString() & " of " & _all.Count.ToString() & " servers"
        End Sub

        Private Shared Function Matches(server As ProtonLogical, query As String) As Boolean
            Return Contains(server.Name, query) _
                OrElse Contains(server.City, query) _
                OrElse Contains(server.Country, query) _
                OrElse Contains(server.CountryCode, query) _
                OrElse Contains(server.FeatureText, query)
        End Function

        Private Shared Function Contains(haystack As String, needle As String) As Boolean
            If String.IsNullOrEmpty(haystack) Then Return False
            Return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0
        End Function

    End Class
End Namespace
