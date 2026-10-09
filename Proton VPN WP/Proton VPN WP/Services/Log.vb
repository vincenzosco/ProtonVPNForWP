' Minimal logging. Deliberately has no API that accepts credentials: the app must
' never write passwords or proofs to the device log (see the plan's Review Focus).
Imports System.Collections.Generic

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
            Dim line As String = DateTime.Now.ToString("[HH:mm:ss] ") & level.ToString() & ": " & message
            System.Diagnostics.Debug.WriteLine(line)
            SyncLock Gate
                Entries.Add(line)
                If Entries.Count > MaxEntries Then Entries.RemoveAt(0)
            End SyncLock
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
