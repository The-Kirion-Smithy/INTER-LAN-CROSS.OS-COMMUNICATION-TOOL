# Native client certificate identity and pairing trust

**Sources:** `src/InterLan.Application/PinnedTransportFactory.cs`, `ClientPairingStateStore.cs`, `PairedClientSessionManager.cs`; `src/InterLan.Desktop/NativePinnedHttp.cs` and `JoinPanel.cs`.

## Trust boundary
An INTER-LAN LAN advertisement or hostname is an **untrusted locator**, not proof of server identity. Before native enrollment, the user must independently confirm the owner server's certificate SHA-256 fingerprint through a trusted channel. Once established, the native client pins that fingerprint. Do not configure a `ServerCertificateValidationCallback` that accepts every certificate, even temporarily.

`PinnedTransportFactory` uses the SHA-256 hash of the raw peer certificate and compares it to the pinned 32-byte value using a constant-time comparison. It disables proxy use and enforces a connection timeout. The native `NativePinnedHttp` flow requires an HTTPS origin, strips colon/dash/space separators from fingerprints and requires 64 hex digits.

## Native join sequence
1. Obtain the owner server's HTTPS origin and 64-character certificate fingerprint directly from the owner, not merely from discovery.
2. Input them into **Join trusted server**. Verify the TLS peer fingerprint before any invite or device enrollment secret is sent.
3. Enter the owner-issued one-use invite, identity and device name. Submit the server-backed join request.
4. Wait for the owner to approve this specific device. Pending status is not proof of authorization.
5. Exchange the approved request with its one-time enrollment secret. Server returns session and device-bound credential for renewal.
6. Persist the pairing only via the supported protected `ClientPairingStateStore`; the pairing tuple includes server ID, HTTPS origin, pinned certificate SHA, device ID, device credential, device name and pairing timestamp.
7. After restart, the `PairedClientSessionManager.LoadAndRenewAsync` path renews the device session against the same pinned server certificate.
8. Unpair/revoke must ask the server to revoke the device when reachable, then clear local state. Treat a failed remote revocation as unconfirmed, not as a server-side success.

## OS security reality
The project has platform-dependent key protection. Windows uses DPAPI-backed key handling in the existing state store. The code documents that Linux/macOS pairing storage may otherwise keep a key alongside ciphertext; the current native onboarding disables durable pairing on those platforms pending a protected OS keychain adapter. Do **not** claim equivalent platform security or enable a weak fallback to meet a release date.

Local encryption does not replace OS account security. Restrict local pairing file access, protect full-disk backups and never commit pairing state or key material. Device credential rotation and session revocation should invalidate old credentials and active transports.

## Certificate rotation and recovery
A pinned fingerprint mismatch is a trust event, not a network hiccup. Stop connection attempts; require out-of-band owner verification and deliberate re-pairing when a server certificate legitimately changes. Do not silently replace a pin with the newly advertised value, and do not re-use a credential bound to an unrelated server identity.

## Failure scenarios
- **Wrong host:** validate HTTPS origin and network reachability, do not remove TLS checks.
- **Certificate mismatch:** stop and verify with owner.
- **Unknown/consumed invite:** request a new owner invitation.
- **Join pending:** wait for an authenticated owner decision.
- **Revoked device:** clear active session and require authorized re-enrollment.
- **Corrupt pairing file:** preserve diagnostics without logging secrets, then re-enroll deliberately.
- **Lost owner database/certificate:** restore the canonical owner data and certificate from secure backup; do not impersonate old server identity.

**Unverified boundaries:** No native runtime tests, OS matrix checks or security audits were run for this reference document.
