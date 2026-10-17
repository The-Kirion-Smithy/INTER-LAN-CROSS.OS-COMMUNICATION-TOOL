using InterLan.Application;
using InterLan.Contracts;
using InterLan.Infrastructure;

var failures = new List<string>();

void Check(bool condition, string name)
{
    if (condition)
    {
        Console.WriteLine($"PASS {name}");
    }
    else
    {
        Console.WriteLine($"FAIL {name}");
        failures.Add(name);
    }
}

async Task<bool> ThrowsAsync<T>(Func<Task> action) where T : Exception
{
    try
    {
        await action();
        return false;
    }
    catch (T)
    {
        return true;
    }
}

var root = Path.Combine(Path.GetTempPath(), "interlan-p1-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);

try
{
    var cert1 = ServerCertificateManager.LoadOrCreate(root);
    var cert2 = ServerCertificateManager.LoadOrCreate(root);

    Check(cert1.Sha256Fingerprint == cert2.Sha256Fingerprint, "persistent TLS certificate fingerprint survives reload");
    Check(cert1.Certificate.HasPrivateKey && cert2.Certificate.HasPrivateKey, "TLS certificate reload preserves usable private key");
    Check(File.Exists(cert1.PrivateKeyPath) && new FileInfo(cert1.PrivateKeyPath).Length > 0, "server private key persists on owner host");
    Check(cert1.Sha256Fingerprint.Length == 64 && cert1.Sha256Fingerprint.All(Uri.IsHexDigit), "certificate fingerprint is SHA-256 hex");

    var announcement = new DiscoveryAnnouncement(
        LanDiscoveryProtocol.ProtocolId,
        Guid.NewGuid(),
        "P1 Test Server",
        7443,
        cert1.Sha256Fingerprint);

    var encoded = LanDiscoveryProtocol.Serialize(announcement);
    var decoded = LanDiscoveryProtocol.Parse(encoded);
    Check(decoded == announcement, "LAN discovery announcement round-trip");

    var malformedRejected = false;
    try
    {
        LanDiscoveryProtocol.Parse("{\"protocol\":\"evil\"}"u8);
    }
    catch (InvalidDataException)
    {
        malformedRejected = true;
    }
    Check(malformedRejected, "malformed/foreign discovery announcement fails closed");

    var database = new SqliteDatabase(Path.Combine(root, "p1.db"));
    await database.InitializeAsync();
    var store = new EnrollmentStore(database);

    var owner = await store.BootstrapOwnerAsync(
        "P1 Test Server",
        "kirch",
        "Kirch",
        "correct horse battery staple",
        Path.Combine(root, "files"),
        7443,
        true);

    Check(owner.ServerId != Guid.Empty && owner.OwnerUserId != Guid.Empty, "bootstrap creates canonical server and owner");

    var persistedRuntimeSettings = await new ServerSettingsStore(database).LoadAsync(root);
    Check(
        persistedRuntimeSettings.Port == 7443 &&
        persistedRuntimeSettings.DiscoveryEnabled &&
        persistedRuntimeSettings.ClientApprovalRequired &&
        persistedRuntimeSettings.StoragePath == Path.GetFullPath(Path.Combine(root, "files")),
        "bootstrap persists canonical server runtime settings");

    var secondBootstrapRejected = await ThrowsAsync<InvalidOperationException>(() =>
        store.BootstrapOwnerAsync(
            "Other",
            "other",
            "Other",
            "another long password",
            Path.Combine(root, "files2"),
            7444,
            false));
    Check(secondBootstrapRejected, "second server-owner bootstrap rejected");

    var ownerSession = await store.LoginOwnerAsync(
        "kirch",
        "correct horse battery staple",
        TimeSpan.FromHours(1));

    Check(ownerSession.Role == "OWNER" && ownerSession.DeviceId is null, "owner login creates owner session");

    var badPasswordRejected = await ThrowsAsync<UnauthorizedAccessException>(() =>
        store.LoginOwnerAsync("kirch", "definitely-wrong", TimeSpan.FromHours(1)));
    Check(badPasswordRejected, "bad owner password rejected");

    var invite = await store.CreateInviteAsync(owner.OwnerUserId, TimeSpan.FromMinutes(30));

    await using (var connection = database.OpenConnection())
    {
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT token_hash FROM invite_tokens WHERE invite_id = $id;";
        query.Parameters.AddWithValue("$id", invite.InviteId.ToString("D"));
        var persisted = Convert.ToString(await query.ExecuteScalarAsync())!;
        Check(persisted == SecretCodec.HashToken(invite.InviteToken), "invite persisted as deterministic hash");
        Check(!persisted.Contains(invite.InviteToken, StringComparison.Ordinal), "invite plaintext is not persisted");
    }

    var enrollmentSecret = SecretCodec.NewToken();
    var join = await store.SubmitJoinAsync(new SubmitJoinRequest(
        invite.InviteToken,
        "member1",
        "Member One",
        "Android Chrome",
        "android-web",
        enrollmentSecret));

    Check(join.Status == "PENDING", "valid invite creates pending join request");

    var pending = await store.ListPendingJoinsAsync(owner.OwnerUserId);
    Check(pending.Count == 1 && pending[0].RequestId == join.RequestId, "owner can enumerate pending join requests");

    var reusedInviteRejected = await ThrowsAsync<UnauthorizedAccessException>(() =>
        store.SubmitJoinAsync(new SubmitJoinRequest(
            invite.InviteToken,
            "member2",
            "Member Two",
            "Laptop",
            "windows",
            SecretCodec.NewToken())));
    Check(reusedInviteRejected, "consumed invite cannot be replayed");

    var decision = await store.DecideJoinAsync(owner.OwnerUserId, join.RequestId, approve: true);
    Check(decision.Status == "APPROVED" && decision.DeviceId is not null && decision.UserId is not null, "owner approval creates member and device");

    var devices = await store.ListDevicesAsync(owner.OwnerUserId);
    Check(devices.Count == 1 && devices[0].DeviceId == decision.DeviceId, "owner can enumerate approved devices");

    var memberSession = await store.ExchangeApprovedJoinAsync(
        join.RequestId,
        enrollmentSecret,
        TimeSpan.FromHours(2));

    Check(memberSession.Role == "MEMBER" && memberSession.DeviceId == decision.DeviceId, "approved device exchanges enrollment secret for member session");
    Check(!string.IsNullOrWhiteSpace(memberSession.DeviceCredential), "first approved exchange returns durable device credential exactly to client");

    await using (var connection = database.OpenConnection())
    {
        await using var credential = connection.CreateCommand();
        credential.CommandText = "SELECT credential_hash FROM devices WHERE device_id = $id;";
        credential.Parameters.AddWithValue("$id", decision.DeviceId!.Value.ToString("D"));
        var persistedCredentialHash = Convert.ToString(await credential.ExecuteScalarAsync())!;
        Check(
            persistedCredentialHash == SecretCodec.HashToken(memberSession.DeviceCredential!),
            "device credential persists only as server-side hash");
        Check(
            !persistedCredentialHash.Contains(memberSession.DeviceCredential!, StringComparison.Ordinal),
            "device credential plaintext is not persisted server-side");
    }

    var secondDeviceInvite = await store.CreateInviteAsync(owner.OwnerUserId, TimeSpan.FromMinutes(30));
    var secondDeviceSecret = SecretCodec.NewToken();
    var secondDeviceJoin = await store.SubmitJoinAsync(new SubmitJoinRequest(
        secondDeviceInvite.InviteToken,
        "member1",
        "Member One",
        "Member One Laptop",
        "windows",
        secondDeviceSecret));

    var secondDeviceDecision = await store.DecideJoinAsync(
        owner.OwnerUserId,
        secondDeviceJoin.RequestId,
        approve: true,
        existingUserId: decision.UserId!.Value);

    Check(
        secondDeviceDecision.UserId == decision.UserId &&
        secondDeviceDecision.DeviceId != decision.DeviceId,
        "owner can attach another approved device to an existing user");

    var secondDeviceSession = await store.ExchangeApprovedJoinAsync(
        secondDeviceJoin.RequestId,
        secondDeviceSecret,
        TimeSpan.FromHours(2));

    Check(
        secondDeviceSession.UserId == memberSession.UserId &&
        secondDeviceSession.DeviceId == secondDeviceDecision.DeviceId,
        "additional approved device receives session for existing user identity");

    var currentDeviceSecurity = await store.GetCurrentDeviceSecurityAsync(
        memberSession.UserId,
        memberSession.DeviceId!.Value);

    Check(
        currentDeviceSecurity.DeviceId == memberSession.DeviceId &&
        currentDeviceSecurity.CredentialCreatedUtc is not null,
        "current paired device exposes credential lifecycle security state");

    var memberSessions = await store.ListOwnSessionsAsync(
        memberSession.UserId,
        memberSession.SessionId);

    Check(
        memberSessions.Count >= 2 &&
        memberSessions.Count(session => session.Current) == 1 &&
        memberSessions.Single(session => session.Current).SessionId == memberSession.SessionId,
        "user session inventory identifies current session across multiple devices");

    await store.RevokeOwnSessionAsync(
        memberSession.UserId,
        secondDeviceSession.SessionId);

    Check(
        await store.ValidateSessionAsync(secondDeviceSession.BearerToken) is null &&
        await store.ValidateSessionAsync(memberSession.BearerToken) is not null,
        "user can revoke another own session without revoking current session");

    var secondExchangeRejected = await ThrowsAsync<UnauthorizedAccessException>(() =>
        store.ExchangeApprovedJoinAsync(join.RequestId, enrollmentSecret, TimeSpan.FromHours(2)));
    Check(secondExchangeRejected, "enrollment exchange is one-use");

    await using (var connection = database.OpenConnection())
    {
        await using var query = connection.CreateCommand();
        query.CommandText = "SELECT token_hash FROM device_sessions WHERE session_id = $id;";
        query.Parameters.AddWithValue("$id", memberSession.SessionId.ToString("D"));
        var persisted = Convert.ToString(await query.ExecuteScalarAsync())!;
        Check(persisted == SecretCodec.HashToken(memberSession.BearerToken), "session persisted as hash");
        Check(!persisted.Contains(memberSession.BearerToken, StringComparison.Ordinal), "session plaintext is not persisted");
    }

    var principal = await store.ValidateSessionAsync(memberSession.BearerToken);
    Check(principal is not null && principal.Role == "MEMBER", "active member session validates");

    var devicesAfterActivity = await store.ListDevicesAsync(owner.OwnerUserId);
    Check(
        devicesAfterActivity.Single(device => device.DeviceId == decision.DeviceId).LastSeenUtc is not null,
        "session validation updates durable device activity");

    var pairingRestartDatabase = new SqliteDatabase(Path.Combine(root, "p1.db"));
    await pairingRestartDatabase.InitializeAsync();
    var pairingRestartStore = new EnrollmentStore(pairingRestartDatabase);
    var pairingAfterRestart = await pairingRestartStore.ValidateSessionAsync(memberSession.BearerToken);
    Check(
        pairingAfterRestart is not null &&
        pairingAfterRestart.UserId == decision.UserId &&
        pairingAfterRestart.DeviceId == decision.DeviceId,
        "approved client pairing and active session survive server restart");

    var renewedAfterRestart = await pairingRestartStore.RenewDeviceSessionAsync(
        decision.DeviceId!.Value,
        memberSession.DeviceCredential!,
        TimeSpan.FromHours(1));
    Check(
        renewedAfterRestart.UserId == decision.UserId &&
        renewedAfterRestart.DeviceId == decision.DeviceId &&
        !string.IsNullOrWhiteSpace(renewedAfterRestart.BearerToken),
        "paired device credential mints fresh session after server restart");

    var pairingStatePath = Path.Combine(root, "client-pairing", "pairing.state");
    var pairingState = new ClientPairingState(
        owner.ServerId,
        "https://127.0.0.1:7443",
        cert1.Sha256Fingerprint,
        decision.DeviceId.Value,
        memberSession.DeviceCredential!,
        "Android Chrome",
        DateTimeOffset.UtcNow);

    var pairingStateStore = new ClientPairingStateStore();
    await pairingStateStore.SaveAsync(pairingStatePath, pairingState);
    var restoredPairing = await new ClientPairingStateStore().LoadAsync(pairingStatePath);
    Check(restoredPairing == pairingState, "client pairing state survives client-store reopen");
    var pairingBytes = await File.ReadAllBytesAsync(pairingStatePath);
    Check(
        !System.Text.Encoding.UTF8.GetString(pairingBytes).Contains(memberSession.DeviceCredential!, StringComparison.Ordinal),
        "client pairing state does not persist credential as plaintext");

    var devicesAfterRestart = await pairingRestartStore.ListDevicesAsync(owner.OwnerUserId);
    Check(
        devicesAfterRestart.Count == 2 &&
        devicesAfterRestart.Any(device =>
            device.DeviceId == decision.DeviceId &&
            device.RevokedUtc is null) &&
        devicesAfterRestart.Any(device =>
            device.DeviceId == secondDeviceDecision.DeviceId &&
            device.UserId == decision.UserId &&
            device.RevokedUtc is null),
        "all approved device records survive server restart");

    var rotatedSession = await pairingRestartStore.RotateDeviceCredentialAsync(
        decision.UserId!.Value,
        renewedAfterRestart.SessionId,
        decision.DeviceId!.Value,
        TimeSpan.FromHours(1));

    Check(
        !string.IsNullOrWhiteSpace(rotatedSession.DeviceCredential) &&
        rotatedSession.DeviceCredential != memberSession.DeviceCredential,
        "device credential rotation returns a new credential");

    var oldCredentialRejected = await ThrowsAsync<UnauthorizedAccessException>(() =>
        pairingRestartStore.RenewDeviceSessionAsync(
            decision.DeviceId.Value,
            memberSession.DeviceCredential!,
            TimeSpan.FromHours(1)));
    Check(oldCredentialRejected, "rotated device credential invalidates previous credential");

    var priorSessionAfterRotation = await pairingRestartStore.ValidateSessionAsync(
        renewedAfterRestart.BearerToken);
    Check(priorSessionAfterRotation is null, "credential rotation revokes prior device sessions");

    var rotatedPrincipal = await pairingRestartStore.ValidateSessionAsync(
        rotatedSession.BearerToken);
    Check(
        rotatedPrincipal is not null &&
        rotatedPrincipal.DeviceId == decision.DeviceId,
        "credential rotation returns an active replacement session");

    await store.RevokeDeviceAsync(owner.OwnerUserId, decision.DeviceId!.Value);
    var revoked = await store.ValidateSessionAsync(memberSession.BearerToken);
    Check(revoked is null, "device revocation invalidates existing sessions");

    var renewalAfterRevokeRejected = await ThrowsAsync<UnauthorizedAccessException>(() =>
        store.RenewDeviceSessionAsync(
            decision.DeviceId.Value,
            rotatedSession.DeviceCredential!,
            TimeSpan.FromHours(1)));
    Check(renewalAfterRevokeRejected, "revoked device credential cannot mint a new session");

    // Exercise concurrent sessions for the owner actually bootstrapped above.
    var extraOwnerSession = await store.LoginOwnerAsync(
        "kirch",
        "correct horse battery staple",
        TimeSpan.FromHours(1));

    var revokedOthers = await store.RevokeOtherSessionsAsync(
        ownerSession.UserId,
        ownerSession.SessionId);

    Check(
        revokedOthers.Contains(extraOwnerSession.SessionId) &&
        await store.ValidateSessionAsync(extraOwnerSession.BearerToken) is null &&
        await store.ValidateSessionAsync(ownerSession.BearerToken) is not null,
        "revoke-other-sessions preserves current authority only");

    var ownerPrincipal = await store.ValidateSessionAsync(ownerSession.BearerToken);
    Check(ownerPrincipal is not null && ownerPrincipal.Role == "OWNER", "owner session remains valid after member revocation");

    var expiredInvite = await store.CreateInviteAsync(owner.OwnerUserId, TimeSpan.FromMinutes(5));
    await using (var connection = database.OpenConnection())
    {
        await using var expire = connection.CreateCommand();
        expire.CommandText = "UPDATE invite_tokens SET expires_utc = $past WHERE invite_id = $id;";
        expire.Parameters.AddWithValue("$past", DateTimeOffset.UtcNow.AddMinutes(-1).ToString("O"));
        expire.Parameters.AddWithValue("$id", expiredInvite.InviteId.ToString("D"));
        await expire.ExecuteNonQueryAsync();
    }

    var expiredRejected = await ThrowsAsync<UnauthorizedAccessException>(() =>
        store.SubmitJoinAsync(new SubmitJoinRequest(
            expiredInvite.InviteToken,
            "expired-user",
            "Expired User",
            "Expired Device",
            "test",
            SecretCodec.NewToken())));
    Check(expiredRejected, "expired invite is rejected");

    var rejectInvite = await store.CreateInviteAsync(owner.OwnerUserId, TimeSpan.FromMinutes(30));
    var rejectJoin = await store.SubmitJoinAsync(new SubmitJoinRequest(
        rejectInvite.InviteToken,
        "rejected-user",
        "Rejected User",
        "Rejected Device",
        "test",
        SecretCodec.NewToken()));
    var rejectedDecision = await store.DecideJoinAsync(owner.OwnerUserId, rejectJoin.RequestId, approve: false);
    Check(rejectedDecision.Status == "REJECTED", "owner can reject pending join request");

    Check(await store.IsDiscoveryEnabledAsync(), "persisted discovery policy is enabled");

    var reopenedDatabase = new SqliteDatabase(Path.Combine(root, "p1.db"));
    await reopenedDatabase.InitializeAsync();
    var reopenedIdentityStore = new SqliteServerIdentityStore(reopenedDatabase);
    var reopenedIdentity = await reopenedIdentityStore.GetAsync();
    Check(reopenedIdentity == owner, "restart preserves canonical server identity");

    var logoutSession = await store.LoginOwnerAsync(
        "kirch",
        "correct horse battery staple",
        TimeSpan.FromHours(1));

    await store.RevokeOwnSessionAsync(
        logoutSession.UserId,
        logoutSession.SessionId);

    Check(
        await store.ValidateSessionAsync(logoutSession.BearerToken) is null,
        "authenticated self-logout revokes the active session");

    await store.RevokeSessionAsync(owner.OwnerUserId, ownerSession.SessionId);
    var revokedOwner = await store.ValidateSessionAsync(ownerSession.BearerToken);
    Check(revokedOwner is null, "explicit owner-authorized session revocation invalidates token");

    await using (var connection = database.OpenConnection())
    {
        await using var audit = connection.CreateCommand();
        audit.CommandText =
            """
            SELECT COUNT(1)
            FROM audit_events
            WHERE event_type IN (
                'SERVER_BOOTSTRAPPED',
                'INVITE_CREATED',
                'JOIN_REQUESTED',
                'JOIN_APPROVED',
                'JOIN_REJECTED',
                'SESSION_CREATED',
                'DEVICE_REVOKED',
                'SESSION_REVOKED',
                'SESSION_LOGOUT'
            );
            """;
        var auditCount = Convert.ToInt64(await audit.ExecuteScalarAsync());
        Check(auditCount >= 10, "identity/enrollment security events are auditable");
    }
}
finally
{
    try
    {
        Directory.Delete(root, recursive: true);
    }
    catch
    {
        // CI cleanup is best effort.
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"P1 checks failed: {string.Join(", ", failures)}");
    return 1;
}

Console.WriteLine("INTER-LAN P1 LAN/IDENTITY CHECKS: PASS");
return 0;
