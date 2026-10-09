' Proton issues a separate pair of credentials for OpenVPN/IKEv2 that is NOT the
' account password. The app can fetch them from the API when signed in, or the
' user can paste the ones from account.protonvpn.com when offline.
Namespace Models
    Friend NotInheritable Class VpnCredentials
        Friend Property Username As String
        Friend Property Password As String
        Friend Property Source As CredentialSource = CredentialSource.Manual

        Friend ReadOnly Property IsComplete As Boolean
            Get
                Return Not String.IsNullOrEmpty(Username) AndAlso Not String.IsNullOrEmpty(Password)
            End Get
        End Property
    End Class

    Friend Enum CredentialSource
        Manual = 0
        Api = 1
    End Enum
End Namespace
