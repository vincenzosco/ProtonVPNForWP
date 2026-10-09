' Desktop driver for the pure log-file policy.
'
' Compiled by tools/run_log_harness.py against the *shipping*
' Services/LogFilePolicy.vb, so a green run is evidence about what ships. It
' prints one key=value line per vector and leaves the asserting to Python.
Imports System

Module Program
    Sub Main()
        Dim utc As DateTime = New DateTime(2026, 10, 9, 15, 28, 28, DateTimeKind.Utc).AddTicks(7215520L)

        Emit("format", Services.LogFilePolicy.FormatLine("Info", "startup: shell navigated", utc))
        Emit("format_local", Services.LogFilePolicy.FormatLine("Error", "boom", utc.ToLocalTime()))
        Emit("trim65536", Services.LogFilePolicy.ShouldTrim(65536L).ToString())
        Emit("trim65537", Services.LogFilePolicy.ShouldTrim(65537L).ToString())
        Emit("keep1000", Services.LogFilePolicy.KeepCount(1000).ToString())
        Emit("keep3", Services.LogFilePolicy.KeepCount(3).ToString())
        Emit("keep1", Services.LogFilePolicy.KeepCount(1).ToString())
        Emit("newest", String.Join("|", Services.LogFilePolicy.NewestLines(New String() {"a", "b", "c", "d"}, 2)))
        Emit("newest_all", String.Join("|", Services.LogFilePolicy.NewestLines(New String() {"a", "b"}, 5)))
        Emit("newest_zero", String.Join("|", Services.LogFilePolicy.NewestLines(New String() {"a", "b"}, 0)))
    End Sub

    Private Sub Emit(key As String, value As String)
        Console.WriteLine(key & "=" & value.Replace(vbCr, "\r").Replace(vbLf, "\n"))
    End Sub
End Module
