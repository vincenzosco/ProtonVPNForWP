' Turns a chosen server plus Proton's OpenVPN/IKEv2 credentials into exactly the
' values Windows Phone's built-in VPN dialog asks for.
Imports Models

Namespace Services
    Friend NotInheritable Class VpnProfileBuilder

        Private Sub New()
        End Sub

        Friend Shared Function BuildIkeV2(logical As ProtonLogical, credentials As VpnCredentials) As VpnProfile
            Dim profile As New VpnProfile()
            profile.Kind = ProfileKind.IkeV2
            profile.ServerName = BuildDisplayName(logical)
            profile.ServerAddress = logical.HostName
            profile.CountryCode = logical.CountryCode
            profile.Username = If(credentials Is Nothing, Nothing, credentials.Username)
            profile.Password = If(credentials Is Nothing, Nothing, credentials.Password)
            Return profile
        End Function

        Friend Shared Function BuildOpenVpn(logical As ProtonLogical, credentials As VpnCredentials) As VpnProfile
            Dim profile As VpnProfile = BuildIkeV2(logical, credentials)
            profile.Kind = ProfileKind.OpenVpn
            profile.OpenVpnText = profile.ToOpenVpnConfig()
            Return profile
        End Function

        Friend Shared Function Build(kind As ProfileKind, logical As ProtonLogical, credentials As VpnCredentials) As VpnProfile
            Select Case kind
                Case ProfileKind.OpenVpn : Return BuildOpenVpn(logical, credentials)
                Case Else : Return BuildIkeV2(logical, credentials)
            End Select
        End Function

        ''' <summary>
        ''' Proton issues one OpenVPN/IKEv2 credential pair per account; the server is
        ''' chosen by the server address field, not by a username suffix.
        ''' </summary>
        Friend Shared Function BuildDisplayName(logical As ProtonLogical) As String
            If logical Is Nothing Then Return "Proton VPN"
            Return "Proton " & logical.Name
        End Function

    End Class
End Namespace
