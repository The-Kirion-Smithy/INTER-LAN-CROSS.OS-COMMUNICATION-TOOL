using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using InterLan.Testing;

var repositoryRoot = RepositoryLayout.FindRoot();
await using var server = await InterLanServerProcess.StartAsync(repositoryRoot);

using (var tcp = new TcpClient(AddressFamily.InterNetwork))
{
    await tcp.ConnectAsync(IPAddress.Loopback, server.BaseUri.Port)
        .WaitAsync(TimeSpan.FromSeconds(2));
}
Console.WriteLine($"PASS TCP loopback 127.0.0.1:{server.BaseUri.Port}");

using var client = server.CreateClient();
Console.WriteLine($"PASS HTTPS Kestrel health endpoint via {server.BaseUri}");

using var initialInfoResponse = await client.GetAsync("/api/v1/server/info");
if (!initialInfoResponse.IsSuccessStatusCode ||
    initialInfoResponse.Headers.CacheControl?.NoStore != true)
{
    Console.Error.WriteLine("FAIL unconfigured server info is cacheable or unavailable");
    return 1;
}
var before = await initialInfoResponse.Content.ReadFromJsonAsync<JsonElement>();
var beforeServer = before.GetProperty("server");
if (beforeServer.GetProperty("configured").GetBoolean())
{
    Console.Error.WriteLine("FAIL fresh server unexpectedly configured");
    return 1;
}

var fingerprint = before.GetProperty("certificateSha256").GetString();
if (fingerprint is null || fingerprint.Length != 64 || !fingerprint.All(Uri.IsHexDigit))
{
    Console.Error.WriteLine("FAIL server info missing TLS fingerprint");
    return 1;
}

Console.WriteLine("PASS server info publishes pairing fingerprint");

using var bootstrap = await client.PostAsJsonAsync("/api/v1/bootstrap/server", new
{
    serverName = "Network Smoke",
    ownerUsername = "owner",
    ownerDisplayName = "Owner",
    ownerPassword = "network-smoke-password"
});

if (!bootstrap.IsSuccessStatusCode)
{
    Console.Error.WriteLine(
        $"FAIL bootstrap HTTP {(int)bootstrap.StatusCode}: {await bootstrap.Content.ReadAsStringAsync()}");
    return 1;
}

Console.WriteLine("PASS loopback bootstrap endpoint");

using var duplicate = await client.PostAsJsonAsync("/api/v1/bootstrap/server", new
{
    serverName = "Second",
    ownerUsername = "owner2",
    ownerDisplayName = "Owner Two",
    ownerPassword = "network-smoke-password-2"
});

if (duplicate.IsSuccessStatusCode)
{
    Console.Error.WriteLine("FAIL duplicate bootstrap was accepted");
    return 1;
}

Console.WriteLine("PASS duplicate bootstrap fails closed");

using var configuredInfoResponse = await client.GetAsync("/api/v1/server/info");
if (!configuredInfoResponse.IsSuccessStatusCode ||
    configuredInfoResponse.Headers.CacheControl?.NoStore != true)
{
    Console.Error.WriteLine("FAIL configured server info is cacheable or unavailable");
    return 1;
}
var after = await configuredInfoResponse.Content.ReadFromJsonAsync<JsonElement>();
if (!after.GetProperty("server").GetProperty("configured").GetBoolean())
{
    Console.Error.WriteLine("FAIL configured server state not visible after bootstrap");
    return 1;
}

Console.WriteLine("PASS configured server identity survives through real HTTPS API");
Console.WriteLine("INTER-LAN P1 NETWORK SMOKE: PASS");
return 0;
