' The result of a successful Proton sign-in.
'
' AccessToken/RefreshToken/Uid are secret material: they are persisted only through
' CredentialVault (DPAPI protected) and never written to logs or navigation args.
Namespace Models
    Friend NotInheritable Class ProtonSession
        Friend Property Username As String
        Friend Property UserId As String
        Friend Property AccessToken As String
        Friend Property RefreshToken As String
        Friend Property Scope As String
        Friend Property ExpiresIn As Integer
        Friend Property IsTwoFactorPending As Boolean
        Friend Property TwoFactorEnabled As Boolean
        Friend Property IssuedUtc As DateTime = DateTime.UtcNow

        Friend ReadOnly Property IsAuthenticated As Boolean
            Get
                Return Not String.IsNullOrEmpty(AccessToken)
            End Get
        End Property

        Friend ReadOnly Property HasExpired As Boolean
            Get
                If ExpiresIn <= 0 Then Return False
                Return DateTime.UtcNow > IssuedUtc.AddSeconds(ExpiresIn - 60)
            End Get
        End Property

        Friend Sub Clear()
            Username = Nothing
            UserId = Nothing
            AccessToken = Nothing
            RefreshToken = Nothing
            Scope = Nothing
            ExpiresIn = 0
            IsTwoFactorPending = False
            TwoFactorEnabled = False
        End Sub
    End Class
End Namespace
