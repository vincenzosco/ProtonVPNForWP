' TLS 1.3 key schedule (RFC 8446 section 7.1) over HKDF-SHA256.
'
' Every secret here is a Derive-Secret or an HKDF-Extract; the labels and the
' transcript hash are exactly what RFC 8446 fixes, and nothing else. The desktop
' harness recomputes each value with an independent Python implementation, and the
' live handshake in tools/run_tls_harness.py is what proves the labels themselves
' are right (the server's Finished only verifies if they are).
Namespace Tls
    Friend NotInheritable Class TlsKeys

        Private Const HashLen As Integer = 32

        Private Sub New()
        End Sub

        Private Shared Function Hash(transcript As Byte()) As Byte()
            If transcript Is Nothing Then transcript = New Byte() {}
            Return Crypto.Sha256.Hash(transcript)
        End Function

        ''' <summary>Derive-Secret(secret, label, Messages) with Messages already hashed.</summary>
        Private Shared Function DeriveSecret(secret As Byte(), label As String, transcriptHash As Byte()) As Byte()
            Return Crypto.Hkdf.ExpandLabel(secret, label, transcriptHash, HashLen)
        End Function

        ''' <summary>early_secret = HKDF-Extract(salt=0, IKM=0).</summary>
        Friend Shared Function EarlySecret() As Byte()
            Return Crypto.Hkdf.Extract(New Byte(HashLen - 1) {}, New Byte(HashLen - 1) {})
        End Function

        ''' <summary>handshake_secret = HKDF-Extract(Derive-Secret(early, "derived", ""), ECDHE).</summary>
        Friend Shared Function HandshakeSecret(earlySecret As Byte(), sharedSecret As Byte()) As Byte()
            Return Crypto.Hkdf.Extract(DeriveSecret(earlySecret, "derived", Hash(New Byte() {})), sharedSecret)
        End Function

        ''' <summary>master_secret = HKDF-Extract(Derive-Secret(handshake, "derived", ""), 0).</summary>
        Friend Shared Function MasterSecret(handshakeSecret As Byte()) As Byte()
            Return Crypto.Hkdf.Extract(DeriveSecret(handshakeSecret, "derived", Hash(New Byte() {})),
                                       New Byte(HashLen - 1) {})
        End Function

        ''' <summary>c hs traffic / s hs traffic from the handshake secret.</summary>
        Friend Shared Function ClientHandshakeTrafficSecret(handshakeSecret As Byte(), transcriptHash As Byte()) As Byte()
            Return DeriveSecret(handshakeSecret, "c hs traffic", transcriptHash)
        End Function

        Friend Shared Function ServerHandshakeTrafficSecret(handshakeSecret As Byte(), transcriptHash As Byte()) As Byte()
            Return DeriveSecret(handshakeSecret, "s hs traffic", transcriptHash)
        End Function

        ''' <summary>c ap traffic / s ap traffic from the master secret.</summary>
        Friend Shared Function ClientApplicationTrafficSecret(masterSecret As Byte(), transcriptHash As Byte()) As Byte()
            Return DeriveSecret(masterSecret, "c ap traffic", transcriptHash)
        End Function

        Friend Shared Function ServerApplicationTrafficSecret(masterSecret As Byte(), transcriptHash As Byte()) As Byte()
            Return DeriveSecret(masterSecret, "s ap traffic", transcriptHash)
        End Function

        ''' <summary>Traffic key = HKDF-Expand-Label(secret, "key", "", 16).</summary>
        Friend Shared Function TrafficKey(secret As Byte()) As Byte()
            Return Crypto.Hkdf.ExpandLabel(secret, "key", New Byte() {}, 16)
        End Function

        ''' <summary>IV = HKDF-Expand-Label(secret, "iv", "", 12).</summary>
        Friend Shared Function TrafficIv(secret As Byte()) As Byte()
            Return Crypto.Hkdf.ExpandLabel(secret, "iv", New Byte() {}, 12)
        End Function

        ''' <summary>finished_key = HKDF-Expand-Label(secret, "finished", "", 32).</summary>
        Friend Shared Function FinishedKey(secret As Byte()) As Byte()
            Return Crypto.Hkdf.ExpandLabel(secret, "finished", New Byte() {}, HashLen)
        End Function

        ''' <summary>verify_data = HMAC(finished_key, Transcript-Hash(Messages)).</summary>
        Friend Shared Function FinishedVerifyData(finishedKey As Byte(), transcriptHash As Byte()) As Byte()
            Return Crypto.Hmac.Compute(finishedKey, transcriptHash)
        End Function

    End Class
End Namespace
