' Dashboard view model: account state, catalogue state and the quick pick.
Imports System.Collections.Generic
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Models
Imports Proton_VPN_WP.Services

Namespace ViewModels
    Friend NotInheritable Class HomeViewModel
        Inherits ViewModelBase

        Private _accountLine As String = "Not signed in"
        Private _networkLine As String = "Checking network…"
        Private _catalogueLine As String = "Loading servers…"
        Private _selectedServer As ProtonLogical
        Private _noticeLine As String

        Friend Property AccountLine As String
            Get
                Return If(_accountLine, String.Empty)
            End Get
            Set(value As String)
                SetProperty(_accountLine, value, "AccountLine")
            End Set
        End Property

        Friend Property NetworkLine As String
            Get
                Return If(_networkLine, String.Empty)
            End Get
            Set(value As String)
                SetProperty(_networkLine, value, "NetworkLine")
            End Set
        End Property

        Friend Property CatalogueLine As String
            Get
                Return If(_catalogueLine, String.Empty)
            End Get
            Set(value As String)
                SetProperty(_catalogueLine, value, "CatalogueLine")
            End Set
        End Property

        Friend Property NoticeLine As String
            Get
                Return If(_noticeLine, String.Empty)
            End Get
            Set(value As String)
                SetProperty(_noticeLine, value, "NoticeLine")
                RaisePropertyChanged("HasNotice")
            End Set
        End Property

        Friend ReadOnly Property HasNotice As Boolean
            Get
                Return Not String.IsNullOrEmpty(_noticeLine)
            End Get
        End Property

        Friend Property SelectedServer As ProtonLogical
            Get
                Return _selectedServer
            End Get
            Set(value As ProtonLogical)
                If SetProperty(_selectedServer, value, "SelectedServer") Then
                    RaisePropertyChanged("SelectionLine")
                    RaisePropertyChanged("HasSelection")
                End If
            End Set
        End Property

        Friend ReadOnly Property HasSelection As Boolean
            Get
                Return _selectedServer IsNot Nothing
            End Get
        End Property

        Friend ReadOnly Property SelectionLine As String
            Get
                If _selectedServer Is Nothing Then Return "No server selected yet."
                Return _selectedServer.Name & " · " & _selectedServer.LocationText
            End Get
        End Property

        Friend Sub RefreshAccount()
            If AppServices.Current.IsSignedIn Then
                AccountLine = "Signed in as " & AppServices.Current.Username
            Else
                AccountLine = "Not signed in — offline server list in use"
            End If
            NetworkLine = ConnectivityService.ActiveNetworkName() & If(ConnectivityService.HasInternetAccess(), " · online", " · offline")
        End Sub

        Friend Async Function InitializeAsync(forceRefresh As Boolean) As Task(Of ApiResult(Of IList(Of ProtonLogical)))
            IsBusy = True
            StatusMessage = "Loading servers…"
            Try
                Dim result As ApiResult(Of IList(Of ProtonLogical)) = Await AppServices.Current.Catalog.LoadAsync(forceRefresh)
                Dim source As String = DescribeSource(AppServices.Current.Catalog.Source)
                CatalogueLine = AppServices.Current.Catalog.Count.ToString() & " servers · " & source

                If Not String.IsNullOrEmpty(result.Message) Then
                    NoticeLine = result.Message
                Else
                    NoticeLine = Nothing
                End If

                Dim settings As AppSettings = AppServices.Current.Settings.Load()
                Dim last As ProtonLogical = AppServices.Current.Catalog.FindByName(settings.LastServerName)
                If last IsNot Nothing Then SelectedServer = last

                StatusMessage = Nothing
                Return result
            Finally
                IsBusy = False
            End Try
        End Function

        ''' <summary>Least loaded server, preferring the country used last time.</summary>
        Friend Function QuickPick() As ProtonLogical
            Dim settings As AppSettings = AppServices.Current.Settings.Load()
            Dim catalog As ServerCatalog = AppServices.Current.Catalog
            Dim best As ProtonLogical = catalog.FindFastestInCountry(settings.LastCountryCode, settings.OnlyFreeServers)
            If best Is Nothing Then best = catalog.FindFastestInCountry(Nothing, settings.OnlyFreeServers)
            Return best
        End Function

        Friend Shared Function DescribeSource(source As CatalogueSource) As String
            Select Case source
                Case CatalogueSource.Api : Return "Live from Proton"
                Case CatalogueSource.Cache : Return "Cached from Proton"
                Case CatalogueSource.Bundled : Return "Offline list"
                Case Else : Return "Unknown"
            End Select
        End Function

    End Class
End Namespace
