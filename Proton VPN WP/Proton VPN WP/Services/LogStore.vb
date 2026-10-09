' The file sink behind Log: one file under the app's local folder, appended to,
' trimmed when it grows past the policy's ceiling.
'
' Every failure here is swallowed on purpose. The log exists to explain a broken
' app, so it must never be the reason the app breaks: if storage cannot be
' written the app still starts, and the only trace is on the debug channel.
'
' Location: ApplicationData.Current.LocalFolder\logs\app.log. That is the app's
' own folder, reachable from the desktop with the SDK's ISETool (see
' docs/BUILD.md). SharedLocalFolder is *not* an option on WP8.1 -- it does not
' exist in the platform metadata.
Imports System.Collections.Generic
Imports System.Text
Imports System.Threading.Tasks
Imports Windows.Storage
Imports Windows.Storage.FileProperties

Namespace Services
    Friend NotInheritable Class LogStore

        Private Shared ReadOnly Pending As New List(Of String)()
        Private Shared ReadOnly Gate As New Object()
        Private Shared _draining As Boolean = False

        Private Sub New()
        End Sub

        ''' <summary>Folder holding the log, for display and for ISETool retrieval.</summary>
        Friend Shared Function FolderPath() As String
            Return ApplicationData.Current.LocalFolder.Path & "\" & LogFilePolicy.FolderName
        End Function

        Friend Shared Function FilePath() As String
            Return FolderPath() & "\" & LogFilePolicy.FileName
        End Function

        ''' <summary>
        ''' Queues a line and returns immediately. The caller is usually the UI thread
        ''' on its way to rendering something; a log write must never be why it waits.
        ''' </summary>
        Friend Shared Sub Enqueue(line As String)
            If line Is Nothing Then Return

            SyncLock Gate
                Pending.Add(line)
                If _draining Then Return
                _draining = True
            End SyncLock

            ' Fire and forget. Assigning the Task is what keeps this warning-free.
            Dim drain As Task = DrainAsync()
        End Sub

        ''' <summary>
        ''' Writes everything queued as a single append, so concurrent callers cannot
        ''' interleave inside the file, then trims if the file has outgrown the policy.
        ''' </summary>
        Friend Shared Async Function DrainAsync() As Task
            Dim batch As String
            SyncLock Gate
                If Pending.Count = 0 Then
                    _draining = False
                    Return
                End If
                Dim joined As New StringBuilder()
                For Each queued As String In Pending
                    joined.Append(queued)
                Next
                batch = joined.ToString()
                Pending.Clear()
            End SyncLock

            Try
                Dim file As StorageFile = Await OpenFileAsync()
                Await FileIO.AppendTextAsync(file, batch)
                Await TrimIfNeededAsync(file)
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine("log: could not append to the file: " & ex.Message)
            End Try

            ' Lines queued while this batch was in flight must not stay stuck behind
            ' the _draining flag.
            Dim remaining As Boolean
            SyncLock Gate
                remaining = Pending.Count > 0
                If Not remaining Then _draining = False
            End SyncLock
            If remaining Then Await DrainAsync()
        End Function

        ''' <summary>The whole file, for the diagnostics page. Never throws.</summary>
        Friend Shared Async Function ReadAllAsync() As Task(Of String)
            Try
                Dim file As StorageFile = Await OpenFileAsync()
                Return Await FileIO.ReadTextAsync(file)
            Catch ex As Exception
                Return "log: could not be read: " & ex.Message
            End Try
        End Function

        Friend Shared Async Function ClearAsync() As Task
            Try
                Dim file As StorageFile = Await OpenFileAsync()
                Await FileIO.WriteTextAsync(file, String.Empty)
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine("log: could not be cleared: " & ex.Message)
            End Try
        End Function

        Private Shared Async Function OpenFileAsync() As Task(Of StorageFile)
            Dim folder As StorageFolder = Await ApplicationData.Current.LocalFolder.CreateFolderAsync(
                LogFilePolicy.FolderName, CreationCollisionOption.OpenIfExists)
            Return Await folder.CreateFileAsync(LogFilePolicy.FileName, CreationCollisionOption.OpenIfExists)
        End Function

        ''' <summary>
        ''' Drops the oldest half once the file passes the ceiling, so a long-running
        ''' install cannot fill the app's storage with diagnostics.
        ''' </summary>
        Private Shared Async Function TrimIfNeededAsync(file As StorageFile) As Task
            Dim properties As BasicProperties = Await file.GetBasicPropertiesAsync()
            If Not LogFilePolicy.ShouldTrim(CLng(properties.Size)) Then Return

            Dim text As String = Await FileIO.ReadTextAsync(file)
            Dim lines As String() = text.Split(New String() {vbCrLf}, StringSplitOptions.RemoveEmptyEntries)
            Dim keep As String() = LogFilePolicy.NewestLines(lines, LogFilePolicy.KeepCount(lines.Length))

            Dim kept As New StringBuilder()
            For Each line As String In keep
                kept.Append(line)
                kept.Append(vbCrLf)
            Next
            Await FileIO.WriteTextAsync(file, kept.ToString())
        End Function

    End Class
End Namespace
