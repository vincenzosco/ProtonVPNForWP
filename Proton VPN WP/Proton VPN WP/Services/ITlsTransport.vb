' A byte stream the TLS client runs over.
'
' Kept behind an interface for the same reason IRandomSource is: the shipping
' implementation wraps a WinRT StreamSocket, which exists only on the phone, while
' the desktop harness supplies a System.Net.Sockets implementation and exercises
' the exact client code.
Imports System.Threading.Tasks

Namespace Tls
    Friend Interface ITlsTransport
        ''' <summary>Reads up to <paramref name="count"/> bytes; 0 means the other end closed.</summary>
        Function ReadAsync(buffer As Byte(), offset As Integer, count As Integer) As Task(Of Integer)
        Function WriteAsync(buffer As Byte(), offset As Integer, count As Integer) As Task
        Sub Close()
    End Interface
End Namespace
