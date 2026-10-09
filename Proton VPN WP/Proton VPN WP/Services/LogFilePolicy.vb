' Pure retention and formatting rules for the on-device log file.
'
' Deliberately free of WinRT and of the rest of the app: tools/run_log_harness.py
' compiles this exact file with the desktop VB compiler and checks its vectors,
' which is only possible while it depends on nothing platform-specific. The level
' arrives as a String rather than a LogLevel for the same reason.
'
' Timestamps are UTC in the invariant round-trip format. Never read one of these
' back with the culture-sensitive DateTime overloads: measured on a UTC+2 machine,
' DateTime.TryParse over "…T15:28:28Z" yields 17:28:28 with Kind=Local, silently
' shifting the instant by the offset.
Imports System
Imports System.Globalization

Namespace Services
    Friend NotInheritable Class LogFilePolicy

        ''' <summary>Folder inside the app's local folder that holds the log.</summary>
        Friend Const FolderName As String = "logs"

        ''' <summary>Name of the single log file.</summary>
        Friend Const FileName As String = "app.log"

        ''' <summary>Once the file passes this many bytes, the oldest half is dropped.</summary>
        Friend Const MaxBytes As Long = 65536L

        Private Sub New()
        End Sub

        ''' <summary>
        ''' One log line, UTC and newline terminated:
        ''' <c>[2026-10-09T15:28:28.7215520Z] Info: message</c>.
        ''' </summary>
        Friend Shared Function FormatLine(level As String, message As String, utcNow As DateTime) As String
            Return "[" & utcNow.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture) & "] " &
                   level & ": " & If(message, String.Empty) & vbCrLf
        End Function

        Friend Shared Function ShouldTrim(sizeBytes As Long) As Boolean
            Return sizeBytes > MaxBytes
        End Function

        ''' <summary>How many of the newest lines survive a trim. Never zero.</summary>
        Friend Shared Function KeepCount(lineCount As Integer) As Integer
            Return Math.Max(1, lineCount \ 2)
        End Function

        ''' <summary>The newest <paramref name="keep"/> lines, or all of them when there are fewer.</summary>
        Friend Shared Function NewestLines(lines As String(), keep As Integer) As String()
            If lines Is Nothing OrElse lines.Length = 0 Then Return New String() {}
            If keep <= 0 Then Return New String() {}
            If lines.Length <= keep Then Return lines

            Dim newest(keep - 1) As String
            Array.Copy(lines, lines.Length - keep, newest, 0, keep)
            Return newest
        End Function

    End Class
End Namespace
