# Owner storage recovery: SQLite WAL and TLS identity

**Purpose:** a conservative **cold backup/restore** of the existing INTER-LAN canonical owner server, not a tested online-backup tool. Grounding: `src/InterLan.Server/InterLanServerHost.cs`, `src/InterLan.Infrastructure/SqliteDatabase.cs`, `ServerCertificateManager.cs`, and `src/InterLan.Desktop/OwnerServerRuntime.cs`.

## What must survive together

The server data directory, normally `INTERLAN_DATA_DIR` or the resolved owner setting, holds `interlan.db`, the database's possible WAL/SHM sidecars and the owner TLS identity pair:
- `server-cert.pem`: public server certificate;
- `server-key.pem`: **sensitive private key**;
- `interlan.db` and, while active, potentially `interlan.db-wal` and `interlan.db-shm`.

The SQLite DB holds server identity, users, sessions, devices, pairings, conversations and authority/migration state. The certificate/private key is what existing pinned native clients trust. **Restoring a DB without its corresponding certificate and private key can break trust and must not be treated as a transparent restore.**

## Preconditions and stop-the-world requirements

1. Identify the actual **data directory** from active runtime configuration, not only a remembered UI folder. `InterLanServerHost.BuildAsync` resolves `InterLanServerHostOptions.DataDirectory`, then `INTERLAN_DATA_DIR`, then `AppContext.BaseDirectory/data`.
2. Warn connected users of maintenance. Stop the owner host from its desktop management surface, or stop the independently hosted server process through normal service administration.
3. **Confirm the server process is fully stopped** and no other process is writing to the DB. A button click without an observed shutdown is not sufficient.
4. Protect backup destination with appropriate owner-only filesystem permissions and, preferably, disk encryption. Storage snapshots contain user messages, identity hashes and metadata.
5. Record current server HTTPS address, data directory, certificate fingerprint shown through an authenticated/trusted owner channel, and the accepted Git SHA/migration level. Do not paste keys or tokens into incident tickets.

## Cold backup — Windows PowerShell example

This example intentionally copies the **whole stopped directory**, rather than only the main SQLite file. Adjust both locations explicitly. Do **not** run while owner server is still writing.

~~~powershell
$ErrorActionPreference = "Stop"
$DataDir = "C:\InterLanData"              # Replace with actual owner server directory
$BackupRoot = "D:\SecureInterLanBackups"  # Replace with protected destination

if (-not (Test-Path "$DataDir\interlan.db")) {
    throw "No interlan.db at the selected owner data path."
}
if (-not (Test-Path "$DataDir\server-cert.pem") -or
    -not (Test-Path "$DataDir\server-key.pem")) {
    throw "TLS certificate/private key pair is incomplete. Stop and investigate."
}
New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
$Destination = Join-Path $BackupRoot ("interlan-cold-" + (Get-Date -Format "yyyyMMdd-HHmmss"))
Copy-Item -LiteralPath $DataDir -Destination $Destination -Recurse -Force

$required = @("interlan.db", "server-cert.pem", "server-key.pem")
foreach ($filename in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $Destination $filename))) {
        throw "Backup incomplete: $filename"
    }
}
Write-Host "Cold backup completed to $Destination"
~~~

**Copy semantics:** This is a cold directory copy and requires writer quiescence. It does not by itself prove restored application correctness or that the copied archive is encrypted. Keep a second protected/offline copy according to local policy.

## Cold restore procedure

1. Stop the owner server and confirm no writers. **Never overwrite a running database.**
2. Preserve the existing broken data directory under a separate incident location for examination; do not delete it first.
3. Select a complete trusted backup whose database and PEM identity pair came from the same server. Check required filenames and filesystem ACLs.
4. Restore into the same intended data path (or deliberately configure a new path) **as a complete unit**. Do not mix a certificate from one backup with a private key from another.
5. Start the canonical host. `SqliteDatabase.InitializeAsync` enables WAL and checks/executes missing embedded migrations; schema startup errors must stop operations and prompt investigation.
6. Verify the server reports the expected server ID and certificate fingerprint through a trusted channel. A mismatch requires explicit owner approval and planned re-pairing; **never turn off client certificate pinning**.
7. On a controlled test device, verify owner login, existing approved devices and authorized direct/group reads; verify revocation still prevents access. Do not run acceptance tests against an untrusted restored snapshot with real secrets.
8. Log the backup provenance, stopped/started times, resulting SHA/migration level, observed validation and outstanding risks without exposing credentials.

## SQLite WAL rules and online alternatives

`SqliteDatabase.InitializeAsync` enables `PRAGMA journal_mode=WAL`. In WAL mode, committed transactions may reside in `interlan.db-wal` until checkpointing. `interlan.db-shm` holds shared-memory coordination state; it is not an independent logical database. Copying `interlan.db` alone while writers are active is not a consistent snapshot.

For online backup, use a correctly implemented **SQLite backup API**, a supported snapshot mechanism with database consistency guarantees, or a verified offline maintenance window. INTER-LAN has not yet integrated a first-class online-backup/recovery tool, and this runbook does **not** claim one exists. Do not use a raw live file copy as a shortcut.

## Partial TLS identity loss — fail closed

If only `server-cert.pem` or only `server-key.pem` survives, treat it as an **incident**, not first-run setup. The source hardening tracked in issue #72 rejects this partial state rather than silently creating a replacement identity. Restore both from the same trusted backup. If a rotation is unavoidable, document the reason and notify/pair clients through a deliberate out-of-band certificate-fingerprint confirmation process.

## Restore audit and verification limits

Record whether these were actually performed: server fully stopped, backup source selected, permissions checked, complete restore, expected server ID, expected SHA-256 fingerprint, database migrations, owner login, device session renewal/revocation, direct/group read authorization, and connection recovery.

No backup or restore was executed in producing this document. It is a source-backed recovery procedure, **not** proof of recovered-data integrity, business-continuity RTO/RPO, production readiness or a passed security review.
