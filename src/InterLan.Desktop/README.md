# INTER-LAN Native Desktop — current operator guide

Source lane: Nox / parent [#19](https://github.com/The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL/issues/19). This describes the code on the Nox feature branch, **not** an accepted release. Build: NOT RUN. Runtime/manual verification: NOT RUN. CI: NOT REQUESTED.

## Owner server

1. Launch the .NET 10 Avalonia desktop project and open **Owner server**.
2. Choose the durable data folder with **Choose data folder** or type its absolute local filesystem path. Keep this folder safe; it contains the canonical SQLite data and TLS server identity including the private key.
3. Select **Start owner server**. This starts the existing embedded \`OwnerServerRuntime\` / \`InterLanServerHost\`, not a second server process. Default HTTPS port is 7443, subject to the canonical persisted settings.
4. The app connects to the loopback HTTPS origin using a certificate pin taken from the running server's in-process public certificate descriptor. The displayed SHA-256 is intended to be shared with trusted clients independently. **Do not distribute the private key**.
5. If unconfigured, enter server name, owner username, display name, a password of at least 12 characters and its confirmation; choose **Create the owner account**. Bootstrap calls \`POST /api/v1/bootstrap/server\` on **127.0.0.1 only**; the backend enforces the one-time bootstrap and loopback gate.
6. If already configured, sign in using **Sign in as owner**. Starting the process never grants an owner session. Owner privileges come only from authenticated backend owner login.
7. Issue a one-time invitation and deliver it privately. Refresh pending join requests, approve/reject selected entries, inspect/revoke devices, list/revoke own owner sessions, and load/update persisted server settings through authorized routes.
8. Changes to listener/storage configuration expose the actual server-returned \`RestartRequired\` status. Stop then start the embedded server to load persisted settings. After stop/restart the same data folder is reused; no source code resets the DB or generates a replacement certificate deliberately.

Owner session bearer remains in memory, not a desktop configuration file. Password fields are masked and cleared after sign-in. Do not assume disk identity survived restart until that path has been manually verified. Port conflicts and server startup failures are surfaced as errors, not reported as success.

## Native client on Windows

1. In **Join trusted server**, enter the owner's HTTPS server origin and independently verified **SHA-256 certificate fingerprint**. Optionally scan multicast advertisements and copy only an advertised address; broadcast names/addresses/hashes are **untrusted**.
2. Select **Verify HTTPS certificate**. The pinned HTTP transport only accepts a server presenting the exact entered certificate SHA-256 and checks the info endpoint; no global ignore-TLS-certificate policy exists.
3. Enter an unconsumed invite, desired username/display name, and device name; select **Submit device approval request**. Keep the app open while the owner approves that request; the one-time enrollment secret exists in memory only and is not recoverable after restart.
4. Select **Complete owner-approved pairing**. The server returns a device-bound credential only after approval. On Windows the existing pairing-state store saves it encrypted with a DPAPI-backed AES key. If secure persistence fails after server exchange, the app reports that condition and retains a temporary session so the device can be revoked; it does not claim durable restoration.
5. After an approved session, the **Native messaging** tab contains the canonical directory, direct inbox, history, message send/retry, receipts, reply/edit/delete controls, server-side search, pin/archive and authenticated SignalR reconnect. Messages are shown as committed only after server confirmation.
6. **Sign out session** revokes the active bearer while retaining the paired device credential for future **Restore saved device session**. **Rotate approved device credential** uses the canonical API and updates protected local state. **Revoke this device and unpair** revokes server authority before deleting local protected pairing state; offline unpair is not falsely acknowledged.

There is currently one durable saved pairing profile (\`ClientPairingStateStore.GetDefaultPath()\`), not a multi-server account manager. Pairing to another server can replace the existing local profile; it does not automatically revoke the old device on its old server. User should unpair first.

## Platform and integration limits

- Windows: DPAPI protection path exists in the repository but was not executed in this campaign.
- Linux/macOS: desktop owner mode is composed from shared Avalonia/.NET source; native pairing is **disabled** until the key protector is integrated and checked. The existing fallback writes the AES key next to the protected pairing file and is not considered equivalent security. See [#40](https://github.com/The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL/issues/40).
- Groups, membership administration, attachments and file transfer: **not wired**. The current \`main\` typed application client/server maps direct conversations only. Integration requires Jane's accepted canonical endpoints and authorization policy, tracked at [#38](https://github.com/The-Kirion-Smithy/INTER-LAN-CROSS.OS-COMMUNICATION-TOOL/issues/38). No fake group/file UI or fabricated transfers are presented.
- Discovery is best effort multicast; manual HTTPS address entry remains supported when multicast/firewall is unavailable.
- A pending unexchanged join secret is memory-only; closing the app before approval completion requires a fresh invitation.
- Realtime delivery and server-side authorization are reused from existing application/server code; all behavior here still requires exact-head compilation, real Windows/manual smoke, Linux/macOS owner smoke, security review, and integration checks before acceptance.
