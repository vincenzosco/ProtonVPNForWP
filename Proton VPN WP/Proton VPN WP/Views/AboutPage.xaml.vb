' About page.
Imports System.Threading.Tasks
Imports Proton_VPN_WP.Services
Imports Windows.UI.Xaml
Imports Windows.UI.Xaml.Controls
Imports Windows.UI.Xaml.Navigation

Namespace Views
    Partial Friend NotInheritable Class AboutPage
        Inherits Page

        Private Const RepositoryUrl As String = "https://github.com/vincenzosco/ProtonVPNForWP"
        Private Const ProtonUrl As String = "https://protonvpn.com"

        Public Sub New()
            InitializeComponent()
        End Sub

        Protected Overrides Sub OnNavigatedTo(e As NavigationEventArgs)
            MyBase.OnNavigatedTo(e)
            VersionText.Text = "Version " & VersionString()
            RepoText.Text = RepositoryUrl
        End Sub

        Private Shared Function VersionString() As String
            Try
                Dim version As PackageVersion = Windows.ApplicationModel.Package.Current.Id.Version
                Return version.Major.ToString() & "." & version.Minor.ToString() & "." &
                       version.Build.ToString() & "." & version.Revision.ToString()
            Catch ex As Exception
                Log.Warn("Package version unavailable: " & ex.Message)
                Return "unknown"
            End Try
        End Function

        Private Async Sub OnOpenRepoClick(sender As Object, e As RoutedEventArgs)
            Await OpenAsync(RepositoryUrl, "the project page")
        End Sub

        Private Async Sub OnOpenProtonClick(sender As Object, e As RoutedEventArgs)
            Await OpenAsync(ProtonUrl, "protonvpn.com")
        End Sub

        Private Async Function OpenAsync(url As String, what As String) As Task
            Try
                Dim opened As Boolean = Await Windows.System.Launcher.LaunchUriAsync(New Uri(url))
                If Not opened Then Throw New InvalidOperationException("no handler")
                StatusText.Text = "Opened " & what & "."
            Catch ex As Exception
                Log.Info("Could not open a link: " & ex.Message)
                StatusText.Text = "No browser accepted the link. The address is shown above."
            End Try
        End Function

    End Class
End Namespace
