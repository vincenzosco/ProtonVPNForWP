' A ready-to-use VPN profile the user installs into Windows Phone's BUILT-IN VPN.
'
' Windows Phone 8.1 exposes no VPN API to third-party apps, so the app's job ends
' at producing exactly the fields the OS asks for and walking the user through them.
Imports System.Collections.Generic
Imports System.Text

Namespace Models

    Friend Enum ProfileKind
        IkeV2 = 0
        L2tp = 1
        OpenVpn = 2
    End Enum

    Friend NotInheritable Class VpnProfile
        Friend Property Kind As ProfileKind = ProfileKind.IkeV2
        Friend Property ServerName As String
        Friend Property ServerAddress As String
        Friend Property Username As String
        Friend Property Password As String
        Friend Property CountryCode As String
        Friend Property OpenVpnText As String

        Friend ReadOnly Property KindText As String
            Get
                Select Case Kind
                    Case ProfileKind.IkeV2 : Return "IKEv2 / IPsec"
                    Case ProfileKind.L2tp : Return "L2TP / IPsec"
                    Case ProfileKind.OpenVpn : Return "OpenVPN"
                    Case Else : Return "Unknown"
                End Select
            End Get
        End Property

        ''' <summary>
        ''' The value the operating system's own "VPN type" field expects for this
        ''' profile. It must follow Kind: the built-in Windows Phone client takes
        ''' IKEv2 (or L2TP), while an OpenVPN profile is an export for an external
        ''' client and never a value the built-in dialog accepts.
        ''' </summary>
        Friend ReadOnly Property VpnTypeValue As String
            Get
                Select Case Kind
                    Case ProfileKind.OpenVpn : Return "OpenVPN"
                    Case ProfileKind.L2tp : Return "L2TP / IPsec"
                    Case Else : Return "IKEv2"
                End Select
            End Get
        End Property

        ''' <summary>
        ''' The ordered list of values to type into Settings -> VPN -> Add, which is
        ''' what the setup page shows the user step by step.
        ''' </summary>
        Friend Function SetupFields() As IList(Of KeyValuePair(Of String, String))
            Dim fields As New List(Of KeyValuePair(Of String, String))()
            fields.Add(New KeyValuePair(Of String, String)("Server name", ServerName))
            fields.Add(New KeyValuePair(Of String, String)("Server address", ServerAddress))
            fields.Add(New KeyValuePair(Of String, String)("VPN type", VpnTypeValue))
            fields.Add(New KeyValuePair(Of String, String)("Username", Username))
            fields.Add(New KeyValuePair(Of String, String)("Password", Password))
            Return fields
        End Function

        ''' <summary>Builds an OpenVPN client configuration file for this server.</summary>
        Friend Function ToOpenVpnConfig() As String
            Dim sb As New StringBuilder()
            sb.AppendLine("client")
            sb.AppendLine("dev tun")
            sb.AppendLine("proto udp")
            sb.AppendLine("remote " & ServerAddress & " 1194")
            sb.AppendLine("resolv-retry infinite")
            sb.AppendLine("nobind")
            sb.AppendLine("persist-key")
            sb.AppendLine("persist-tun")
            sb.AppendLine("remote-cert-tls server")
            sb.AppendLine("auth-user-pass")
            sb.AppendLine("verb 3")
            sb.AppendLine("cipher AES-256-GCM")
            sb.AppendLine("auth SHA512")
            sb.AppendLine("key-direction 1")
            sb.AppendLine("<tls-auth>")
            sb.AppendLine("-----BEGIN OpenVPN Static key V1-----")
            sb.AppendLine("(paste the tls-auth key from Proton's OpenVPN configuration download)")
            sb.AppendLine("-----END OpenVPN Static key V1-----")
            sb.AppendLine("</tls-auth>")
            Return sb.ToString()
        End Function

    End Class
End Namespace
