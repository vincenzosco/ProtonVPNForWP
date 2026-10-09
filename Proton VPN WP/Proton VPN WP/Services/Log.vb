' Minimal logging. Deliberately has no API that accepts credentials: the app must
' never write passwords or proofs to the device log (see the plan's Review Focus).
Imports System.Collections.Generic
Imports System.Globalization

Namespace Services
    Friend Enum LogLevel
        DebugLevel = 0
        Info = 1
        Warning = 2
        [Error] = 3
    End Enum

    Friend NotInheritable Class Log

        Private Shared ReadOnly Entries As New List(Of String)()
        Private Shared ReadOnly Gate As New Object()
        Private Const MaxEntries As Integer = 400

        Private Sub New()
        End Sub

        Friend Shared Sub Info(message As String)
            Write(LogLevel.Info, message)
        End Sub

        Friend Shared Sub Warn(message As String)
            Write(LogLevel.Warning, message)
        End Sub

        Friend Shared Sub [Error](message As String)
            Write(LogLevel.Error, message)
        End Sub

        Private Shared Sub Write(level As LogLevel, message As String)
            ' Two renderings on purpose. The short local-time line is what the
            ' diagnostics page shows and what a debugger shows; the file gets a precise
            ' UTC timestamp that still means something once it is pasted into a report.
            Dim display As String = DateTime.Now.ToString("[HH:mm:ss] ", CultureInfo.InvariantCulture) &
                                    level.ToString() & ": " & message
            Dim persisted As String = LogFilePolicy.FormatLine(level.ToString(), message, DateTime.UtcNow)

            System.Diagnostics.Debug.WriteLine(display)
            SyncLock Gate
                Entries.Add(display)
                If Entries.Count > MaxEntries Then Entries.RemoveAt(0)
            End SyncLock

            ' Fire and forget, and never let the file sink surface: this is the
            ' diagnostic path, so it must not be able to take the app down.
            Try
                LogStore.Enqueue(persisted)
            Catch ex As Exception
                System.Diagnostics.Debug.WriteLine("log: could not queue a line for the file: " & ex.Message)
            End Try
        End Sub

        ''' <summary>Returns a copy of the in-memory log for the diagnostics page.</summary>
        Friend Shared Function Snapshot() As IReadOnlyList(Of String)
            SyncLock Gate
                Return Entries.ToArray()
            End SyncLock
        End Function

        Friend Shared Sub Clear()
            SyncLock Gate
                Entries.Clear()
            End SyncLock
        End Sub

    End Class
End Namespace
