' MVVM base. Uses explicit backing fields because VS2013's VB 12 has no
' auto-implemented read-only properties.
Imports System.Collections.Generic
Imports System.ComponentModel
Imports System.Runtime.CompilerServices

Namespace ViewModels
    Friend MustInherit Class ViewModelBase
        Implements INotifyPropertyChanged

        Public Event PropertyChanged As PropertyChangedEventHandler Implements INotifyPropertyChanged.PropertyChanged

        Private _isBusy As Boolean
        Private _statusMessage As String

        Friend Property IsBusy As Boolean
            Get
                Return _isBusy
            End Get
            Set(value As Boolean)
                SetProperty(_isBusy, value, "IsBusy")
                RaisePropertyChanged("IsNotBusy")
            End Set
        End Property

        Friend ReadOnly Property IsNotBusy As Boolean
            Get
                Return Not _isBusy
            End Get
        End Property

        Friend Property StatusMessage As String
            Get
                Return _statusMessage
            End Get
            Set(value As String)
                SetProperty(_statusMessage, value, "StatusMessage")
            End Set
        End Property

        ''' <summary>Sets a property and raises change notifications for it.</summary>
        Protected Function SetProperty(Of T)(ByRef storage As T, value As T, propertyName As String) As Boolean
            If EqualityComparer(Of T).Default.Equals(storage, value) Then Return False
            storage = value
            RaisePropertyChanged(propertyName)
            Return True
        End Function

        ''' <summary>Raises change notifications for several properties at once.</summary>
        Protected Sub RaiseProperties(ParamArray propertyNames As String())
            For Each name As String In propertyNames
                RaisePropertyChanged(name)
            Next
        End Sub

        Protected Sub RaisePropertyChanged(<CallerMemberName> Optional propertyName As String = Nothing)
            If propertyName Is Nothing Then Return
            RaiseEvent PropertyChanged(Me, New PropertyChangedEventArgs(propertyName))
        End Sub

    End Class
End Namespace
