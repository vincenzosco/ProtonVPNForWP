' X.509 parsing and chain verification, enough for the chain Proton serves.
'
' Only the pieces this app validates are parsed: the public key and signature of a
' certificate, its subjectAltNames, its validity window and its CA bit. The chain
' is verified leaf-to-anchor with PKCS#1 v1.5 SHA-256 (the scheme Proton's chain
' uses), and the anchor is the ISRG Root X1 public key pinned below -- not the leaf,
' which rotates.
Imports System.Collections.Generic
Imports System.Globalization

Namespace Crypto
    Friend NotInheritable Class X509

        Friend NotInheritable Class Certificate
            Friend Raw As Byte()
            ''' <summary>The exact TLV of tbsCertificate: what the signature covers.</summary>
            Friend Tbs As Byte()
            Friend Signature As Byte()
            Friend Modulus As Byte()
            Friend Exponent As Byte()
            Friend SubjectAltNames As New List(Of String)()
            Friend NotBefore As DateTime
            Friend NotAfter As DateTime
            Friend IsCa As Boolean
        End Class

        ' DER OIDs, without the tag and length.
        Private Shared ReadOnly RsaEncryption As Byte() = {&H2A, &H86, &H48, &H86, &HF7, &HD, &H1, &H1, &H1}
        Private Shared ReadOnly SanOid As Byte() = {&H55, &H1D, &H11}
        Private Shared ReadOnly BasicConstraintsOid As Byte() = {&H55, &H1D, &H13}

        ' The trust anchor: the subjectPublicKeyInfo RSA modulus of ISRG Root X1,
        ' SPKI SHA-256 C5+lpZ7tcVwmwQIMcRtPbsQtWLABXhQzejna0wHFr8M= (Let's Encrypt's
        ' published root). Public key material only.
        Private Const AnchorModulusHex As String =
            "ADE82473F41437F39B9E2B57281C87BEDCB7DF38908C6E3CE657A078F775C2A2" &
            "FEF56A6EF6004F28DBDE68866C4493B6B163FD14126BBF1FD2EA319B217ED13" &
            "33CBA48F5DD79DFB3B8FF12F1219A4BC18A8671694A66666C8F7E3C70BFAD29" &
            "2206F3E4C0E680AEE24B8FB7997E94039FD347977C99482353E838AE4F0A6F8" &
            "32ED149578C8074B6DA2FD0388D7B0370211B75F2303CFA8FAEDDDA63ABEB16" &
            "4FC28E114B7ECF0BE8FFB5772EF4B27B4AE04C12250C708D0329A0E15324EC1" &
            "3D9EE19BF10B34A8C3F89A36151DEAC870794F46371EC2EE26F5B9881E1895C3" &
            "4796C76EF3B906279E6DBA49A2F26C5D010E10EDED9108E16FBB7F7A8F7C7E5" &
            "0207988F360895E7E237960D36759EFB0E72B11D9BBC03F94905D881DD05B42A" &
            "D641E9AC0176950A0FD8DFD5BD121F352F28176CD298C1A80964776E4737BACE" &
            "AC595E689D7F72D689C50641293E593EDD26F524C911A75AA34C401F46A199B5" &
            "A73A516E863B9E7D72A712057859ED3E5178150B038F8DD02F05B23E7B4A1C4B" &
            "730512FCC6EAE050137C439374B3CA74E78E1F0108D030D45B7136B407BAC1303" &
            "05C48B7823B98A67D608AA2A32982CCBABD83041BA2830341A1D605F11BC2B6F" &
            "0A87C863B46A8482A88DC769A76BF1F6AA53D198FEB38F364DEC82B0D0A28FFF" &
            "7DBE21542D422D0275DE179FE18E77088AD4EE6D98B3AC6DD27516EFFBC64F53" &
            "3434F"

        Private Sub New()
        End Sub

        Friend Shared Function Parse(der As Byte()) As Certificate
            If der Is Nothing Then Return Nothing
            Dim root As Asn1.Element = Asn1.Parse(der)
            If root Is Nothing OrElse root.Tag <> &H30 Then Return Nothing
            If root.Children Is Nothing OrElse root.Children.Count < 3 Then Return Nothing

            Dim tbs As Asn1.Element = root.Children(0)
            Dim signatureValue As Asn1.Element = root.Children(2)
            If tbs Is Nothing OrElse tbs.Children Is Nothing Then Return Nothing
            If signatureValue Is Nothing OrElse signatureValue.Content.Length < 2 Then Return Nothing

            Dim cert As New Certificate()
            cert.Raw = der
            cert.Tbs = tbs.Raw
            Dim signature(signatureValue.Content.Length - 2) As Byte
            Array.Copy(signatureValue.Content, 1, signature, 0, signature.Length)
            cert.Signature = signature

            Dim validity As Asn1.Element = Nothing
            Dim spki As Asn1.Element = Nothing
            Dim extensions As Asn1.Element = Nothing
            For Each child As Asn1.Element In tbs.Children
                If child.Tag = &H30 Then
                    If IsSubjectPublicKeyInfo(child) Then
                        spki = child
                    ElseIf IsValidity(child) Then
                        validity = child
                    End If
                ElseIf child.Tag = &HA3 Then
                    extensions = child
                End If
            Next

            If spki Is Nothing OrElse spki.Children.Count < 2 Then Return Nothing
            Dim keyBits As Asn1.Element = spki.Children(1)
            If keyBits.Content.Length < 2 Then Return Nothing
            Dim rsaDer(keyBits.Content.Length - 2) As Byte
            Array.Copy(keyBits.Content, 1, rsaDer, 0, rsaDer.Length)
            Dim rsa As Asn1.Element = Asn1.Parse(rsaDer)
            If rsa Is Nothing OrElse rsa.Children Is Nothing OrElse rsa.Children.Count < 2 Then Return Nothing
            cert.Modulus = TrimLeadingZeros(rsa.Children(0).Content)
            cert.Exponent = TrimLeadingZeros(rsa.Children(1).Content)

            If validity IsNot Nothing AndAlso validity.Children IsNot Nothing AndAlso validity.Children.Count >= 2 Then
                cert.NotBefore = ParseTime(validity.Children(0))
                cert.NotAfter = ParseTime(validity.Children(1))
            End If

            If extensions IsNot Nothing AndAlso extensions.Children IsNot Nothing AndAlso extensions.Children.Count >= 1 Then
                Dim list As Asn1.Element = extensions.Children(0)
                If list.Children IsNot Nothing Then
                    For Each extension As Asn1.Element In list.Children
                        If extension.Children Is Nothing OrElse extension.Children.Count < 2 Then Continue For
                        Dim oid As Byte() = extension.Children(0).Content
                        ' Extension ::= SEQUENCE { extnID, critical BOOLEAN DEFAULT FALSE, extnValue OCTET STRING }.
                        ' The critical flag is optional, so the OCTET STRING is not always the second child.
                        Dim value As Byte() = Nothing
                        For Each part As Asn1.Element In extension.Children
                            If part.Tag = &H4 Then value = part.Content
                        Next
                        If value Is Nothing Then Continue For
                        If OidEquals(oid, SanOid) Then ParseSubjectAltNames(cert, value)
                        If OidEquals(oid, BasicConstraintsOid) Then ParseBasicConstraints(cert, value)
                    Next
                End If
            End If

            Return cert
        End Function

        ''' <summary>True when the leaf certificate is valid for <paramref name="host"/>.</summary>
        Friend Shared Function MatchesHost(cert As Certificate, host As String) As Boolean
            If cert Is Nothing OrElse String.IsNullOrEmpty(host) Then Return False
            Dim lower As String = host.ToLowerInvariant()
            For Each name As String In cert.SubjectAltNames
                Dim candidate As String = name.ToLowerInvariant()
                If candidate = lower Then Return True
                If candidate.StartsWith("*.") Then
                    Dim suffix As String = candidate.Substring(1)
                    If lower.EndsWith(suffix) Then
                        Dim label As String = lower.Substring(0, lower.Length - suffix.Length)
                        If label.Length > 0 AndAlso Not label.Contains(".") Then Return True
                    End If
                End If
            Next
            Return False
        End Function

        ''' <summary>Nothing when the chain is valid for the host at <paramref name="now"/>, else the reason.</summary>
        Friend Shared Function VerifyChain(chain As List(Of Certificate), host As String, now As DateTime) As String
            If chain Is Nothing OrElse chain.Count = 0 Then Return "The server sent no certificate."

            Dim leaf As Certificate = chain(0)
            If Not MatchesHost(leaf, host) Then Return "The certificate is not valid for " & host & "."
            If now < leaf.NotBefore OrElse now > leaf.NotAfter Then Return "The certificate is outside its validity period."

            For i As Integer = 0 To chain.Count - 2
                If Not VerifySignedBy(chain(i), chain(i + 1).Modulus, chain(i + 1).Exponent) Then
                    Return "The certificate chain is not signed correctly."
                End If
            Next

            If Not VerifySignedBy(chain(chain.Count - 1), CryptoBytes.FromHex(AnchorModulusHex), New Byte() {1, 0, 1}) Then
                Return "The certificate chain does not reach a trusted root."
            End If
            Return Nothing
        End Function

        Private Shared Function VerifySignedBy(cert As Certificate, modulus As Byte(), exponent As Byte()) As Boolean
            Return RsaVerify.VerifyPkcs1Sha256(modulus, exponent, cert.Tbs, cert.Signature)
        End Function

        Private Shared Function IsSubjectPublicKeyInfo(element As Asn1.Element) As Boolean
            If element.Children Is Nothing OrElse element.Children.Count < 2 Then Return False
            Dim algorithm As Asn1.Element = element.Children(0)
            If algorithm.Tag <> &H30 OrElse algorithm.Children Is Nothing OrElse algorithm.Children.Count < 1 Then Return False
            Return OidEquals(algorithm.Children(0).Content, RsaEncryption)
        End Function

        Private Shared Function IsValidity(element As Asn1.Element) As Boolean
            If element.Children Is Nothing OrElse element.Children.Count <> 2 Then Return False
            Dim first As Byte = element.Children(0).Tag
            Dim second As Byte = element.Children(1).Tag
            Return (first = &H17 OrElse first = &H18) AndAlso (second = &H17 OrElse second = &H18)
        End Function

        Private Shared Sub ParseSubjectAltNames(cert As Certificate, der As Byte())
            Dim sequence As Asn1.Element = Asn1.Parse(der)
            If sequence Is Nothing OrElse sequence.Children Is Nothing Then Return
            For Each name As Asn1.Element In sequence.Children
                ' dNSName is [2] IMPLICIT IA5String, primitive: tag 0x82.
                If name.Tag = &H82 Then cert.SubjectAltNames.Add(AsciiOf(name.Content))
            Next
        End Sub

        Private Shared Sub ParseBasicConstraints(cert As Certificate, der As Byte())
            Dim sequence As Asn1.Element = Asn1.Parse(der)
            If sequence Is Nothing OrElse sequence.Children Is Nothing OrElse sequence.Children.Count < 1 Then Return
            Dim first As Asn1.Element = sequence.Children(0)
            If first.Tag = &H1 Then
                cert.IsCa = first.Content.Length >= 1 AndAlso first.Content(0) <> 0
            End If
        End Sub

        Private Shared Function OidEquals(oid As Byte(), expected As Byte()) As Boolean
            If oid Is Nothing OrElse oid.Length <> expected.Length Then Return False
            Return CryptoBytes.ConstantTimeEquals(oid, expected)
        End Function

        Private Shared Function TrimLeadingZeros(data As Byte()) As Byte()
            If data Is Nothing Then Return New Byte() {}
            Dim start As Integer = 0
            While start < data.Length - 1 AndAlso data(start) = 0
                start += 1
            End While
            Dim result(data.Length - start - 1) As Byte
            Array.Copy(data, start, result, 0, result.Length)
            Return result
        End Function

        Private Shared Function AsciiOf(data As Byte()) As String
            If data Is Nothing Then Return String.Empty
            Dim chars(data.Length - 1) As Char
            For i As Integer = 0 To data.Length - 1
                chars(i) = ChrW(CInt(data(i)))
            Next
            Return New String(chars)
        End Function

        ''' <summary>UTCTime (YYMMDDHHMMSSZ) or GeneralizedTime (YYYYMMDDHHMMSSZ), UTC.</summary>
        Private Shared Function ParseTime(element As Asn1.Element) As DateTime
            Dim text As String = AsciiOf(element.Content)
            If element.Tag = &H17 Then
                Dim year As Integer = Integer.Parse(text.Substring(0, 2), CultureInfo.InvariantCulture)
                If year >= 50 Then year += 1900 Else year += 2000
                Return New DateTime(year,
                    Integer.Parse(text.Substring(2, 2), CultureInfo.InvariantCulture),
                    Integer.Parse(text.Substring(4, 2), CultureInfo.InvariantCulture),
                    Integer.Parse(text.Substring(6, 2), CultureInfo.InvariantCulture),
                    Integer.Parse(text.Substring(8, 2), CultureInfo.InvariantCulture),
                    Integer.Parse(text.Substring(10, 2), CultureInfo.InvariantCulture), DateTimeKind.Utc)
            End If
            Return New DateTime(
                Integer.Parse(text.Substring(0, 4), CultureInfo.InvariantCulture),
                Integer.Parse(text.Substring(4, 2), CultureInfo.InvariantCulture),
                Integer.Parse(text.Substring(6, 2), CultureInfo.InvariantCulture),
                Integer.Parse(text.Substring(8, 2), CultureInfo.InvariantCulture),
                Integer.Parse(text.Substring(10, 2), CultureInfo.InvariantCulture),
                Integer.Parse(text.Substring(12, 2), CultureInfo.InvariantCulture), DateTimeKind.Utc)
        End Function

    End Class
End Namespace
