using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using InterLan.Application;
using InterLan.Testing;
using Microsoft.AspNetCore.SignalR.Client;

static HttpClient CreateHttpClient() =>
    TestHttpClientFactory.CreateLoopback();

static HubConnection CreateHub(Uri baseUri, string token)
{
    return new HubConnectionBuilder()
        .WithUrl(new Uri(baseUri, "/hubs/chat"), options =>
        {
            options.Headers["Authorization"] = $"Bearer {token}";
            options.HttpMessageHandlerFactory = _ => new HttpClientHandler
            {
                UseProxy = false,
                ServerCertificateCustomValidationCallback = (_, _, _, _) => true
            };
        })
        .WithAutomaticReconnect(new[]
        {
            TimeSpan.Zero,
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromMilliseconds(500)
        })
        .Build();
}

static HubConnection CreateTicketHub(Uri baseUri, string ticket)
{
    return new HubConnectionBuilder()
        .WithUrl(
            new Uri(baseUri, $"/hubs/chat?ticket={Uri.EscapeDataString(ticket)}"),
            options =>
            {
                options.HttpMessageHandlerFactory = _ => new HttpClientHandler
                {
                    UseProxy = false,
                    ServerCertificateCustomValidationCallback = (_, _, _, _) => true
                };
            })
        .Build();
}

static void Bearer(HttpClient client, string token) =>
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

var repositoryRoot = RepositoryLayout.FindRoot();
await using var server = await InterLanServerProcess.StartAsync(repositoryRoot);
var root = server.DataDirectory;
var baseUri = server.BaseUri;

{
    using var ownerHttp = CreateHttpClient();
    ownerHttp.BaseAddress = baseUri;

    using var bootstrap = await ownerHttp.PostAsJsonAsync("/api/v1/bootstrap/server", new
    {
        serverName = "P2 Realtime Smoke",
        ownerUsername = "owner",
        ownerDisplayName = "Owner",
        ownerPassword = "p2-realtime-owner-password"
    });
    if (!bootstrap.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"FAIL bootstrap {(int)bootstrap.StatusCode}: {await bootstrap.Content.ReadAsStringAsync()}");
        return 1;
    }

    var ownerSession = await (await ownerHttp.PostAsJsonAsync("/api/v1/auth/owner/login", new
    {
        username = "owner",
        password = "p2-realtime-owner-password"
    })).Content.ReadFromJsonAsync<JsonElement>();

    var ownerToken = ownerSession.GetProperty("bearerToken").GetString()!;
    var ownerUserId = ownerSession.GetProperty("userId").GetGuid();
    Bearer(ownerHttp, ownerToken);

    var invite = await (await ownerHttp.PostAsJsonAsync("/api/v1/invites", new { validMinutes = 30 }))
        .Content.ReadFromJsonAsync<JsonElement>();
    var inviteToken = invite.GetProperty("inviteToken").GetString()!;

    var enrollmentSecret = Convert.ToHexString(Guid.NewGuid().ToByteArray()) + Convert.ToHexString(Guid.NewGuid().ToByteArray());

    using var memberHttp = CreateHttpClient();
    memberHttp.BaseAddress = baseUri;

    var joinResponse = await memberHttp.PostAsJsonAsync("/api/v1/join", new
    {
        inviteToken,
        username = "member",
        displayName = "Member",
        deviceName = "Realtime Smoke Device",
        platform = "smoke",
        enrollmentSecret
    });
    var join = await joinResponse.Content.ReadFromJsonAsync<JsonElement>();
    var requestId = join.GetProperty("requestId").GetGuid();

    using var approve = await ownerHttp.PostAsJsonAsync($"/api/v1/join/{requestId:D}/decision", new { approve = true });
    if (!approve.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"FAIL approve {(int)approve.StatusCode}: {await approve.Content.ReadAsStringAsync()}");
        return 1;
    }

    var exchangeResponse = await memberHttp.PostAsJsonAsync("/api/v1/join/exchange", new
    {
        requestId,
        enrollmentSecret
    });
    var memberSession = await exchangeResponse.Content.ReadFromJsonAsync<JsonElement>();
    var memberToken = memberSession.GetProperty("bearerToken").GetString()!;
    var memberUserId = memberSession.GetProperty("userId").GetGuid();
    var memberDeviceId = memberSession.GetProperty("deviceId").GetGuid();
    var deviceCredential = memberSession.GetProperty("deviceCredential").GetString()!;

    if (string.IsNullOrWhiteSpace(deviceCredential))
    {
        Console.Error.WriteLine("FAIL enrollment exchange did not return durable device credential");
        return 1;
    }

    var serverInfo = await ownerHttp.GetFromJsonAsync<JsonElement>("/api/v1/server/info");
    var serverEnvelope = serverInfo.GetProperty("server");
    var pairingStatePath = Path.Combine(root, "client-state", "pairing.state");
    var pairingState = new ClientPairingState(
        serverEnvelope.GetProperty("serverId").GetGuid(),
        baseUri.ToString(),
        serverInfo.GetProperty("certificateSha256").GetString()!,
        memberDeviceId,
        deviceCredential,
        "Realtime Smoke Device",
        DateTimeOffset.UtcNow);

    var pairingStore = new ClientPairingStateStore();
    await pairingStore.SaveAsync(pairingStatePath, pairingState);

    var persistedBytes = await File.ReadAllBytesAsync(pairingStatePath);
    if (System.Text.Encoding.UTF8.GetString(persistedBytes).Contains(deviceCredential, StringComparison.Ordinal))
    {
        Console.Error.WriteLine("FAIL persisted client pairing state exposed device credential plaintext");
        return 1;
    }

    using var pairedConnection = await new PairedClientSessionManager()
        .LoadAndRenewAsync(pairingStatePath);

    var renewedToken = pairedConnection.Session.BearerToken;
    if (pairedConnection.Session.DeviceId != memberDeviceId ||
        pairedConnection.Session.UserId != memberUserId ||
        string.IsNullOrWhiteSpace(renewedToken))
    {
        Console.Error.WriteLine("FAIL persisted pairing restored wrong device identity");
        return 1;
    }

    Console.WriteLine("PASS encrypted client pairing state restores and renews session without re-enrollment");

    var pairingSessionManager = new PairedClientSessionManager();
    using var rotatedConnection = await pairingSessionManager.RotateCredentialAsync(
        pairingStatePath,
        pairedConnection.Session);

    if (string.IsNullOrWhiteSpace(rotatedConnection.Session.DeviceCredential) ||
        rotatedConnection.Session.DeviceCredential == deviceCredential)
    {
        Console.Error.WriteLine("FAIL paired-device credential rotation did not replace credential");
        return 1;
    }

    using var oldCredentialRenewal = await ownerHttp.PostAsJsonAsync(
        "/api/v1/auth/device/renew",
        new
        {
            deviceId = memberDeviceId,
            deviceCredential
        });

    if (oldCredentialRenewal.StatusCode != HttpStatusCode.Unauthorized)
    {
        Console.Error.WriteLine($"FAIL old device credential remained valid after rotation: {(int)oldCredentialRenewal.StatusCode}");
        return 1;
    }

    Console.WriteLine("PASS paired-device rotation invalidates previous durable credential");

    deviceCredential = rotatedConnection.Session.DeviceCredential!;
    memberToken = rotatedConnection.Session.BearerToken;
    Bearer(memberHttp, memberToken);

    using var realtimeTicketResponse = await memberHttp.PostAsync(
        "/api/v1/realtime/ticket",
        content: null);
    if (!realtimeTicketResponse.IsSuccessStatusCode)
    {
        Console.Error.WriteLine(
            $"FAIL realtime ticket issue {(int)realtimeTicketResponse.StatusCode}: {await realtimeTicketResponse.Content.ReadAsStringAsync()}");
        return 1;
    }

    var realtimeTicketEnvelope =
        await realtimeTicketResponse.Content.ReadFromJsonAsync<JsonElement>();
    var realtimeTicket = realtimeTicketEnvelope.GetProperty("ticket").GetString()!;

    await using (var ticketHub = CreateTicketHub(baseUri, realtimeTicket))
    {
        await ticketHub.StartAsync();
        // SignalR StartAsync alone is not evidence that OnConnectedAsync
        // authorized this connection. Verify an actual hub invocation.
        var firstPong = await ticketHub.InvokeAsync<JsonElement>("Ping")
            .WaitAsync(TimeSpan.FromSeconds(5));
        if (firstPong.GetProperty("status").GetString() != "ok")
        {
            Console.Error.WriteLine("FAIL first-use realtime ticket did not authorize hub invocation");
            return 1;
        }
        await ticketHub.StopAsync();
    }

    var ticketReplayRejected = false;
    await using (var replayHub = CreateTicketHub(baseUri, realtimeTicket))
    {
        try
        {
            // A WebSocket connection can appear started before an unauthorized
            // hub abort becomes visible. Successful Ping is the access check.
            await replayHub.StartAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await replayHub.InvokeAsync<JsonElement>("Ping")
                .WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception)
        {
            ticketReplayRejected = true;
        }
    }

    if (!ticketReplayRejected)
    {
        Console.Error.WriteLine("FAIL replayed realtime ticket invoked an authenticated hub method");
        return 1;
    }

    Console.WriteLine("PASS browser realtime ticket authorizes first use and denies replay");

    var dmResponse = await ownerHttp.PostAsJsonAsync("/api/v1/direct", new { otherUserId = memberUserId });
    if (!dmResponse.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"FAIL open DM {(int)dmResponse.StatusCode}: {await dmResponse.Content.ReadAsStringAsync()}");
        return 1;
    }

    var dm = await dmResponse.Content.ReadFromJsonAsync<JsonElement>();
    var conversationId = dm.GetProperty("conversationId").GetGuid();

    await using var memberHub = CreateHub(baseUri, memberToken);
    var received = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);

    memberHub.On<JsonElement>("MessageCreated", message =>
    {
        if (message.TryGetProperty("scopeId", out var scope) &&
            scope.GetGuid() == conversationId)
        {
            received.TrySetResult(message);
        }
    });

    var deliveredReceiptEvent = new TaskCompletionSource<JsonElement>(
        TaskCreationOptions.RunContinuationsAsynchronously);
    var readReceiptEvent = new TaskCompletionSource<JsonElement>(
        TaskCreationOptions.RunContinuationsAsynchronously);

    memberHub.On<JsonElement>("ReceiptUpdated", payload =>
    {
        if (!payload.TryGetProperty("receipts", out var receipts) ||
            receipts.GetArrayLength() == 0)
        {
            return;
        }

        var receipt = receipts[0];
        if (receipt.GetProperty("readUtc").ValueKind == JsonValueKind.Null)
            deliveredReceiptEvent.TrySetResult(payload);
        else
            readReceiptEvent.TrySetResult(payload);
    });

    await memberHub.StartAsync();

    var presenceSnapshot = await memberHttp.GetFromJsonAsync<JsonElement[]>(
        "/api/v1/presence");

    if (presenceSnapshot is null ||
        !presenceSnapshot.Any(item =>
            item.GetProperty("userId").GetGuid() == memberUserId &&
            item.GetProperty("isOnline").GetBoolean() &&
            item.GetProperty("connectionCount").GetInt32() >= 1))
    {
        Console.Error.WriteLine("FAIL connected member missing from presence snapshot");
        return 1;
    }

    Console.WriteLine("PASS connected member appears in authenticated presence snapshot");

    await using var ownerHub = CreateHub(baseUri, ownerToken);
    var typingChanged = new TaskCompletionSource<JsonElement>(
        TaskCreationOptions.RunContinuationsAsynchronously);

    ownerHub.On<JsonElement>("TypingChanged", payload =>
    {
        if (payload.GetProperty("conversationId").GetGuid() == conversationId &&
            payload.GetProperty("userId").GetGuid() == memberUserId)
        {
            typingChanged.TrySetResult(payload);
        }
    });

    await ownerHub.StartAsync();
    await memberHub.InvokeAsync("SetTyping", conversationId, true);

    var typingPayload = await typingChanged.Task.WaitAsync(TimeSpan.FromSeconds(5));
    if (!typingPayload.GetProperty("isTyping").GetBoolean())
    {
        Console.Error.WriteLine("FAIL typing indicator payload did not report active typing");
        return 1;
    }

    Console.WriteLine("PASS authorized typing indicator reaches direct peer in realtime");

    var clientMessageId = Guid.NewGuid();
    using var send = await ownerHttp.PostAsJsonAsync($"/api/v1/direct/{conversationId:D}/messages", new
    {
        clientMessageId,
        body = "realtime hello"
    });

    if (!send.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"FAIL send DM {(int)send.StatusCode}: {await send.Content.ReadAsStringAsync()}");
        return 1;
    }

    var persisted = await send.Content.ReadFromJsonAsync<JsonElement>();
    var persistedMessageId = persisted.GetProperty("messageId").GetGuid();

    var realtime = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

    if (realtime.GetProperty("messageId").GetGuid() != persistedMessageId ||
        realtime.GetProperty("senderUserId").GetGuid() != ownerUserId)
    {
        Console.Error.WriteLine("FAIL realtime payload did not match persisted message");
        return 1;
    }

    Console.WriteLine("PASS persisted message broadcast reaches connected peer through SignalR");

    var history = await memberHttp.GetFromJsonAsync<JsonElement[]>(
        $"/api/v1/direct/{conversationId:D}/messages?limit=100");

    if (history is null || history.Length != 1 || history[0].GetProperty("messageId").GetGuid() != persistedMessageId)
    {
        Console.Error.WriteLine("FAIL persisted realtime message missing from history");
        return 1;
    }

    Console.WriteLine("PASS realtime message is durable before/after broadcast");

    using var deliveredAck = await memberHttp.PostAsync(
        $"/api/v1/messages/{persistedMessageId:D}/delivered",
        content: null);
    if (!deliveredAck.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"FAIL recipient delivery acknowledgement {(int)deliveredAck.StatusCode}: {await deliveredAck.Content.ReadAsStringAsync()}");
        return 1;
    }

    var deliveredState = await ownerHttp.GetFromJsonAsync<JsonElement[]>(
        $"/api/v1/messages/{persistedMessageId:D}/receipts");
    if (deliveredState is null ||
        deliveredState.Length != 1 ||
        deliveredState[0].GetProperty("userId").GetGuid() != memberUserId ||
        deliveredState[0].GetProperty("deliveredUtc").ValueKind == JsonValueKind.Null ||
        deliveredState[0].GetProperty("readUtc").ValueKind != JsonValueKind.Null)
    {
        Console.Error.WriteLine("FAIL delivered receipt state was not persisted correctly");
        return 1;
    }

    var deliveredRealtimeReceipt = await deliveredReceiptEvent.Task.WaitAsync(
        TimeSpan.FromSeconds(5));
    if (deliveredRealtimeReceipt.GetProperty("messageId").GetGuid() != persistedMessageId)
    {
        Console.Error.WriteLine("FAIL realtime delivery receipt event payload mismatch");
        return 1;
    }

    Console.WriteLine("PASS delivery acknowledgement broadcasts realtime receipt state");

    using var readAck = await memberHttp.PostAsync(
        $"/api/v1/messages/{persistedMessageId:D}/read",
        content: null);
    if (!readAck.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"FAIL recipient read acknowledgement {(int)readAck.StatusCode}: {await readAck.Content.ReadAsStringAsync()}");
        return 1;
    }

    var readState = await ownerHttp.GetFromJsonAsync<JsonElement[]>(
        $"/api/v1/messages/{persistedMessageId:D}/receipts");
    if (readState is null ||
        readState.Length != 1 ||
        readState[0].GetProperty("readUtc").ValueKind == JsonValueKind.Null)
    {
        Console.Error.WriteLine("FAIL read receipt state was not persisted correctly");
        return 1;
    }

    var readRealtimeReceipt = await readReceiptEvent.Task.WaitAsync(
        TimeSpan.FromSeconds(5));
    if (readRealtimeReceipt.GetProperty("messageId").GetGuid() != persistedMessageId)
    {
        Console.Error.WriteLine("FAIL realtime read receipt event payload mismatch");
        return 1;
    }

    Console.WriteLine("PASS read acknowledgement broadcasts realtime receipt state");

    await memberHub.StopAsync();

    await Task.Delay(TimeSpan.FromMilliseconds(100));

    var presenceAfterDisconnect = await ownerHttp.GetFromJsonAsync<JsonElement[]>(
        "/api/v1/presence");

    if (presenceAfterDisconnect is null ||
        presenceAfterDisconnect.Any(item =>
            item.GetProperty("userId").GetGuid() == memberUserId))
    {
        Console.Error.WriteLine("FAIL disconnected member remained online in presence snapshot");
        return 1;
    }

    Console.WriteLine("PASS disconnected member leaves authenticated presence snapshot");

    var offlineClientMessageId = Guid.NewGuid();
    using var offlineSend = await ownerHttp.PostAsJsonAsync($"/api/v1/direct/{conversationId:D}/messages", new
    {
        clientMessageId = offlineClientMessageId,
        body = "offline catchup"
    });

    if (!offlineSend.IsSuccessStatusCode)
    {
        Console.Error.WriteLine("FAIL offline message send");
        return 1;
    }

    var catchup = await memberHttp.GetFromJsonAsync<JsonElement[]>(
        $"/api/v1/direct/{conversationId:D}/messages?afterMessageId={persistedMessageId:D}&limit=100");

    if (catchup is null || catchup.Length != 1 ||
        catchup[0].GetProperty("clientMessageId").GetGuid() != offlineClientMessageId)
    {
        Console.Error.WriteLine("FAIL reconnect cursor catch-up");
        return 1;
    }

    Console.WriteLine("PASS disconnected peer catches up by durable cursor without duplicates");

    using var duplicate = await ownerHttp.PostAsJsonAsync($"/api/v1/direct/{conversationId:D}/messages", new
    {
        clientMessageId = offlineClientMessageId,
        body = "offline catchup"
    });

    if (!duplicate.IsSuccessStatusCode)
    {
        Console.Error.WriteLine("FAIL duplicate HTTP send should be idempotent");
        return 1;
    }

    var allHistory = await memberHttp.GetFromJsonAsync<JsonElement[]>(
        $"/api/v1/direct/{conversationId:D}/messages?limit=100");

    if (allHistory is null || allHistory.Length != 2)
    {
        Console.Error.WriteLine("FAIL duplicate send created an additional persisted message");
        return 1;
    }

    Console.WriteLine("PASS duplicate HTTP send does not duplicate persistence");

    await using var mutationHub = CreateHub(baseUri, memberToken);
    var editedEvent = new TaskCompletionSource<JsonElement>(
        TaskCreationOptions.RunContinuationsAsynchronously);

    mutationHub.On<JsonElement>("MessageEdited", message =>
    {
        editedEvent.TrySetResult(message);
    });

    await mutationHub.StartAsync();

    using var mutableSend = await ownerHttp.PostAsJsonAsync(
        $"/api/v1/direct/{conversationId:D}/messages",
        new
        {
            clientMessageId = Guid.NewGuid(),
            body = "mutable realtime message"
        });

    if (!mutableSend.IsSuccessStatusCode)
    {
        Console.Error.WriteLine("FAIL mutable realtime message send");
        return 1;
    }

    var mutableMessage = await mutableSend.Content.ReadFromJsonAsync<JsonElement>();
    var mutableMessageId = mutableMessage.GetProperty("messageId").GetGuid();

    using var editResponse = await ownerHttp.PutAsJsonAsync(
        $"/api/v1/direct/{conversationId:D}/messages/{mutableMessageId:D}",
        new { body = "edited realtime message" });

    if (!editResponse.IsSuccessStatusCode)
    {
        Console.Error.WriteLine(
            $"FAIL realtime message edit {(int)editResponse.StatusCode}: {await editResponse.Content.ReadAsStringAsync()}");
        return 1;
    }

    var editedRealtime = await editedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
    if (editedRealtime.GetProperty("messageId").GetGuid() != mutableMessageId ||
        editedRealtime.GetProperty("body").GetString() != "edited realtime message")
    {
        Console.Error.WriteLine("FAIL realtime edit event payload mismatch");
        return 1;
    }

    Console.WriteLine("PASS direct-message edit broadcasts durable realtime mutation");

    var deletedEvent = new TaskCompletionSource<JsonElement>(
        TaskCreationOptions.RunContinuationsAsynchronously);

    mutationHub.On<JsonElement>("MessageDeleted", message =>
    {
        deletedEvent.TrySetResult(message);
    });

    using var deleteResponse = await ownerHttp.DeleteAsync(
        $"/api/v1/direct/{conversationId:D}/messages/{mutableMessageId:D}");

    if (!deleteResponse.IsSuccessStatusCode)
    {
        Console.Error.WriteLine(
            $"FAIL realtime message delete {(int)deleteResponse.StatusCode}: {await deleteResponse.Content.ReadAsStringAsync()}");
        return 1;
    }

    var deletedRealtime = await deletedEvent.Task.WaitAsync(TimeSpan.FromSeconds(5));
    if (deletedRealtime.GetProperty("messageId").GetGuid() != mutableMessageId)
    {
        Console.Error.WriteLine("FAIL realtime delete event payload mismatch");
        return 1;
    }

    await mutationHub.StopAsync();
    Console.WriteLine("PASS direct-message delete broadcasts realtime tombstone event");

    await using var revokedHub = CreateHub(baseUri, memberToken);
    var revokedHubClosed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
    revokedHub.Closed += _ =>
    {
        revokedHubClosed.TrySetResult(true);
        return Task.CompletedTask;
    };
    await revokedHub.StartAsync();

    using var revokeDevice = await ownerHttp.PostAsync(
        $"/api/v1/devices/{memberDeviceId:D}/revoke",
        content: null);

    if (!revokeDevice.IsSuccessStatusCode)
    {
        Console.Error.WriteLine($"FAIL device revocation {(int)revokeDevice.StatusCode}: {await revokeDevice.Content.ReadAsStringAsync()}");
        return 1;
    }

    await revokedHubClosed.Task.WaitAsync(TimeSpan.FromSeconds(5));
    Console.WriteLine("PASS device revocation aborts an already-connected realtime session");

    using var revokedHistory = await memberHttp.GetAsync(
        $"/api/v1/direct/{conversationId:D}/messages?limit=100");

    if (revokedHistory.StatusCode != HttpStatusCode.Unauthorized)
    {
        Console.Error.WriteLine($"FAIL revoked device retained HTTP authority: {(int)revokedHistory.StatusCode}");
        return 1;
    }

    Console.WriteLine("PASS device revocation removes HTTP authority immediately");

    var messageLimiterSaturated = false;
    for (var index = 0; index < 140; index++)
    {
        using var limitedSend = await ownerHttp.PostAsJsonAsync(
            $"/api/v1/direct/{conversationId:D}/messages",
            new
            {
                clientMessageId = Guid.NewGuid(),
                body = $"rate-limit-{index:D3}"
            });

        if (limitedSend.StatusCode == HttpStatusCode.TooManyRequests)
        {
            messageLimiterSaturated = true;
            break;
        }

        if (!limitedSend.IsSuccessStatusCode)
        {
            Console.Error.WriteLine(
                $"FAIL message limiter saturation request returned {(int)limitedSend.StatusCode}");
            return 1;
        }
    }

    if (!messageLimiterSaturated)
    {
        Console.Error.WriteLine("FAIL message limiter did not saturate within bounded request count");
        return 1;
    }

    Console.WriteLine("PASS authenticated message partition enforces bounded saturation");

    using var secondOwnerHttp = CreateHttpClient();
    secondOwnerHttp.BaseAddress = baseUri;

    using var secondOwnerLogin = await secondOwnerHttp.PostAsJsonAsync(
        "/api/v1/auth/owner/login",
        new
        {
            username = "owner",
            password = "p2-realtime-owner-password"
        });

    if (!secondOwnerLogin.IsSuccessStatusCode)
    {
        Console.Error.WriteLine("FAIL second owner session login");
        return 1;
    }

    var secondOwnerSession =
        await secondOwnerLogin.Content.ReadFromJsonAsync<JsonElement>();
    Bearer(
        secondOwnerHttp,
        secondOwnerSession.GetProperty("bearerToken").GetString()!);

    using var isolatedSend = await secondOwnerHttp.PostAsJsonAsync(
        $"/api/v1/direct/{conversationId:D}/messages",
        new
        {
            clientMessageId = Guid.NewGuid(),
            body = "isolated-session-after-rate-limit"
        });

    if (!isolatedSend.IsSuccessStatusCode)
    {
        Console.Error.WriteLine(
            $"FAIL distinct session inherited another session limiter state: {(int)isolatedSend.StatusCode}");
        return 1;
    }

    Console.WriteLine("PASS distinct authenticated session has isolated message limiter budget");
    Console.WriteLine("INTER-LAN P2 REALTIME SMOKE: PASS");
    return 0;
}
