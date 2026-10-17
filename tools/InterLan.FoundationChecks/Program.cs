using InterLan.Application;
using InterLan.Domain;
using InterLan.Infrastructure;
using Microsoft.Data.Sqlite;

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

var tempRoot = Path.Combine(Path.GetTempPath(), "interlan-foundation-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempRoot);

try
{
    var database = new SqliteDatabase(Path.Combine(tempRoot, "foundation.db"));
    await database.InitializeAsync();

    await using (var connection = database.OpenConnection())
    {
        await using var user = connection.CreateCommand();
        user.CommandText =
            """
            INSERT INTO users (user_id, username, display_name, role, created_utc)
            VALUES ($id, $username, $displayName, 'OWNER', $createdUtc);
            """;
        var ownerId = Guid.NewGuid();
        user.Parameters.AddWithValue("$id", ownerId.ToString("D"));
        user.Parameters.AddWithValue("$username", "owner");
        user.Parameters.AddWithValue("$displayName", "Owner");
        user.Parameters.AddWithValue("$createdUtc", DateTimeOffset.UtcNow.ToString("O"));
        await user.ExecuteNonQueryAsync();

        var identity = new ServerIdentity(Guid.NewGuid(), "Foundation Server", ownerId, DateTimeOffset.UtcNow);
        var store = new SqliteServerIdentityStore(database);
        await store.CreateAsync(identity);

        var loaded = await store.GetAsync();
        Check(loaded == identity, "server identity round-trip");

        var duplicateRejected = false;
        try
        {
            await store.CreateAsync(identity with { ServerId = Guid.NewGuid() });
        }
        catch (SqliteException)
        {
            duplicateRejected = true;
        }

        Check(duplicateRejected, "singleton server identity rejects a second owner-server identity");

        var nonOwnerRejected = false;
        try
        {
            OwnershipRules.ValidateOwner(identity, Guid.NewGuid());
        }
        catch (UnauthorizedAccessException)
        {
            nonOwnerRejected = true;
        }

        Check(nonOwnerRejected, "owner authority is explicit, not inferred");
    }

    var invalidOwnerConfig = new RuntimeConfiguration(
        RuntimeMode.ServerOwner,
        new ServerHostConfiguration(Guid.Empty, Guid.Empty, "", "", "0.0.0.0", 0, true),
        null);
    Check(invalidOwnerConfig.Validate().Count >= 5, "invalid ServerOwner configuration fails closed");

    var validClientConfig = new RuntimeConfiguration(
        RuntimeMode.Client,
        null,
        new ClientRuntimeConfiguration("https://127.0.0.1:7443", null, "device"));
    Check(validClientConfig.Validate().Count == 0, "valid Client configuration passes");

    Check(
        MessageTextPolicy.Normalize("  cafe\u0301  ", 32) == "caf\u00e9",
        "message text normalization preserves NFC");
    Check(
        MessageTextPolicy.Normalize("one\ttwo\nthree\rfour", 64) == "one\ttwo\nthree\rfour",
        "message text allows interior tab, CR and LF");

    var controlsRejected = false;
    try
    {
        _ = MessageTextPolicy.Normalize("alpha\u0000beta", 64);
    }
    catch (ArgumentException)
    {
        controlsRejected = true;
    }
    Check(controlsRejected, "message text rejects NUL control characters");

    var overLengthRejected = false;
    try
    {
        _ = MessageTextPolicy.Normalize("message", 3);
    }
    catch (ArgumentException)
    {
        overLengthRejected = true;
    }
    Check(overLengthRejected, "message text enforces maximum length");

}
finally
{
    try
    {
        Directory.Delete(tempRoot, recursive: true);
    }
    catch
    {
        // CI cleanup is best effort only.
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"Foundation checks failed: {string.Join(", ", failures)}");
    return 1;
}

Console.WriteLine("INTER-LAN P0 FOUNDATION CHECKS: PASS");
return 0;
