' Server models mirroring the schema of Proton's `/vpn/logicals` response -- the
' same schema is used by the bundled offline catalogue so one parser serves both.
'
' These types are Public on purpose: the WinRT XAML binding engine resolves bound
' properties by reflection and cannot see Friend members, so internal visibility
' here would silently render empty list rows.
Imports Proton_VPN_WP.Services
Imports Windows.Data.Json

Namespace Models

    ''' <summary>Feature bit flags reported per logical server by Proton.</summary>
    <Flags>
    Public Enum ServerFeature
        None = 0
        SecureCore = 1
        Tor = 2
        P2P = 4
        Streaming = 8
        IPv6 = 16
        Restricted = 32
    End Enum

    Public Enum ServerStatus
        Down = 0
        Up = 1
        Maintenance = 2
        Unknown = 3
    End Enum

    ''' <summary>Tier the account must hold to use a server.</summary>
    Public Enum ServerTier
        Free = 0
        Basic = 1
        Plus = 2
        Visionary = 3
        Unknown = 4
    End Enum

    ''' <summary>A single physical entry/exit node belonging to a logical server.</summary>
    Public NotInheritable Class ProtonPhysicalServer
        Public Property Id As String
        Public Property EntryIp As String
        Public Property ExitIp As String
        Public Property Domain As String
        Public Property Label As String
        Public Property Status As ServerStatus = ServerStatus.Unknown

        Public Shared Function FromJson(obj As JsonObject) As ProtonPhysicalServer
            If obj Is Nothing Then Return Nothing
            Return New ProtonPhysicalServer With {
                .Id = Json.TryString(obj, "ID"),
                .EntryIp = Json.TryString(obj, "EntryIP"),
                .ExitIp = Json.TryString(obj, "ExitIP"),
                .Domain = Json.TryString(obj, "Domain"),
                .Label = Json.TryString(obj, "Label"),
                .Status = ParseStatus(Json.TryInt(obj, "Status", 3))
            }
        End Function

        Public Shared Function ParseStatus(value As Integer) As ServerStatus
            Select Case value
                Case 0 : Return ServerStatus.Down
                Case 1 : Return ServerStatus.Up
                Case 2 : Return ServerStatus.Maintenance
                Case Else : Return ServerStatus.Unknown
            End Select
        End Function
    End Class

    ''' <summary>A logical server ("US-NY#1") the user can pick.</summary>
    Public NotInheritable Class ProtonLogical
        Public Property Id As String
        Public Property Name As String
        Public Property Domain As String
        Public Property City As String
        Public Property Country As String
        Public Property CountryCode As String
        Public Property Status As ServerStatus = ServerStatus.Unknown
        Public Property Load As Integer
        Public Property Tier As ServerTier = ServerTier.Unknown
        Public Property Features As ServerFeature = ServerFeature.None
        Public Property Servers As New List(Of ProtonPhysicalServer)()

        ''' <summary>Hostname used by the IKEv2/OpenVPN profile.</summary>
        Public ReadOnly Property HostName As String
            Get
                If Not String.IsNullOrEmpty(Domain) Then Return Domain
                For Each physical In Servers
                    If Not String.IsNullOrEmpty(physical.Domain) Then Return physical.Domain
                Next
                Return Name
            End Get
        End Property

        Public ReadOnly Property IsAvailable As Boolean
            Get
                Return Status = ServerStatus.Up
            End Get
        End Property

        ''' <summary>Bound in the server list template.</summary>
        Public ReadOnly Property StatusText As String
            Get
                Select Case Status
                    Case ServerStatus.Up : Return "Online"
                    Case ServerStatus.Maintenance : Return "Maintenance"
                    Case ServerStatus.Down : Return "Offline"
                    Case Else : Return "Unknown"
                End Select
            End Get
        End Property

        ''' <summary>Bound in the server list template, e.g. "23% load".</summary>
        Public ReadOnly Property LoadText As String
            Get
                Return Load.ToString() & "% load"
            End Get
        End Property

        Public ReadOnly Property FeatureText As String
            Get
                Dim parts As New List(Of String)()
                If (Features And ServerFeature.SecureCore) = ServerFeature.SecureCore Then parts.Add("Secure Core")
                If (Features And ServerFeature.Tor) = ServerFeature.Tor Then parts.Add("Tor")
                If (Features And ServerFeature.P2P) = ServerFeature.P2P Then parts.Add("P2P")
                If (Features And ServerFeature.Streaming) = ServerFeature.Streaming Then parts.Add("Streaming")
                If (Features And ServerFeature.IPv6) = ServerFeature.IPv6 Then parts.Add("IPv6")
                If parts.Count = 0 Then Return "Standard"
                Return String.Join(" · ", parts)
            End Get
        End Property

        Public ReadOnly Property TierText As String
            Get
                Select Case Tier
                    Case ServerTier.Free : Return "Free"
                    Case ServerTier.Basic : Return "Basic"
                    Case ServerTier.Plus : Return "Plus"
                    Case ServerTier.Visionary : Return "Visionary"
                    Case Else : Return "Unknown"
                End Select
            End Get
        End Property

        ''' <summary>A human readable location, e.g. "New York, United States".</summary>
        Public ReadOnly Property LocationText As String
            Get
                If String.IsNullOrEmpty(City) Then Return Country
                If String.IsNullOrEmpty(Country) Then Return City
                If String.Equals(City, Country, StringComparison.OrdinalIgnoreCase) Then Return Country
                Return City & ", " & Country
            End Get
        End Property

        Public Shared Function FromJson(obj As JsonObject) As ProtonLogical
            If obj Is Nothing Then Return Nothing

            Dim logical As New ProtonLogical With {
                .Id = Json.TryString(obj, "ID"),
                .Name = Json.TryString(obj, "Name"),
                .Domain = Json.TryString(obj, "Domain"),
                .City = Json.TryString(obj, "City"),
                .Country = Json.TryString(obj, "Country"),
                .Status = ProtonPhysicalServer.ParseStatus(Json.TryInt(obj, "Status", 3)),
                .Load = Json.TryInt(obj, "Load", 0),
                .Tier = ParseTier(Json.TryInt(obj, "Tier", -1)),
                .Features = CType(Json.TryInt(obj, "Features", 0), ServerFeature)
            }

            ' Prefer the explicit exit country code, then the two letter country, then the name.
            logical.CountryCode = Json.TryString(obj, "ExitCountry")
            If String.IsNullOrEmpty(logical.CountryCode) Then logical.CountryCode = Json.TryString(obj, "CountryCode")

            Dim servers As JsonArray = Json.TryArray(obj, "Servers")
            If servers IsNot Nothing Then
                For Each item As IJsonValue In servers
                    If item IsNot Nothing AndAlso item.ValueType = JsonValueType.Object Then
                        Dim physical = ProtonPhysicalServer.FromJson(item.GetObject())
                        If physical IsNot Nothing Then logical.Servers.Add(physical)
                    End If
                Next
            End If

            Return logical
        End Function

        Public Shared Function ParseTier(value As Integer) As ServerTier
            Select Case value
                Case 0 : Return ServerTier.Free
                Case 1 : Return ServerTier.Basic
                Case 2 : Return ServerTier.Plus
                Case 3 : Return ServerTier.Visionary
                Case Else : Return ServerTier.Unknown
            End Select
        End Function

    End Class
End Namespace
