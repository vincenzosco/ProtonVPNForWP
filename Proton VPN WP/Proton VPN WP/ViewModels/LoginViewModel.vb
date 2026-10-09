' Sign-in view model.
'
' The password is passed in as an argument and never stored on the view model, so
' it cannot end up in a binding, a log line or a navigation parameter.
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Models
Imports Proton_VPN_WP.Services

Namespace ViewModels
    Friend NotInheritable Class LoginViewModel
        Inherits ViewModelBase

        Private _username As String
        Private _needsTwoFactor As Boolean
        Private _errorMessage As String
        Private _isOfflineMode As Boolean

        Friend Property Username As String
            Get
                Return _username
            End Get
            Set(value As String)
                SetProperty(_username, value, "Username")
            End Set
        End Property

        Friend Property NeedsTwoFactor As Boolean
            Get
                Return _needsTwoFactor
            End Get
            Set(value As Boolean)
                SetProperty(_needsTwoFactor, value, "NeedsTwoFactor")
            End Set
        End Property

        Friend Property ErrorMessage As String
            Get
                Return _errorMessage
            End Get
            Set(value As String)
                SetProperty(_errorMessage, value, "ErrorMessage")
                RaisePropertyChanged("HasError")
            End Set
        End Property

        Friend ReadOnly Property HasError As Boolean
            Get
                Return Not String.IsNullOrEmpty(_errorMessage)
            End Get
        End Property

        ''' <summary>Set when the user chose to continue without a Proton account.</summary>
        Friend Property IsOfflineMode As Boolean
            Get
                Return _isOfflineMode
            End Get
            Set(value As Boolean)
                SetProperty(_isOfflineMode, value, "IsOfflineMode")
            End Set
        End Property

        ''' <summary>
        ''' Attempts a sign-in. Returns True once a usable session exists; if Proton
        ''' asks for a second factor the view flips NeedsTwoFactor and the caller
        ''' should prompt and call again.
        ''' </summary>
        Friend Async Function SignInAsync(password As String, twoFactorCode As String) As Task(Of Boolean)
            ErrorMessage = Nothing
            IsBusy = True
            StatusMessage = "Contacting Proton…"
            Try
                Dim result As ApiResult(Of ProtonSession) = Await AppServices.Current.Auth.SignInAsync(Username, password, twoFactorCode)

                If Not result.Ok Then
                    ErrorMessage = result.Message
                    StatusMessage = Nothing
                    Return False
                End If

                Dim session As ProtonSession = result.Value
                If session.IsTwoFactorPending Then
                    NeedsTwoFactor = True
                    StatusMessage = result.Message
                    If String.IsNullOrEmpty(StatusMessage) Then
                        StatusMessage = "Enter the 6-digit code from your authenticator app."
                    End If
                    Return False
                End If

                AppServices.Current.Session = session
                NeedsTwoFactor = False
                StatusMessage = "Signed in as " & session.Username & "."
                Return True
            Catch ex As Exception
                Log.Warn("Sign-in failed: " & ex.GetType().Name)
                ErrorMessage = "Sign-in failed. Check your details and try again."
                Return False
            Finally
                IsBusy = False
            End Try
        End Function

    End Class
End Namespace
