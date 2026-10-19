using System.Text.Json;
using System.Text.Json.Serialization;

namespace InterLan.Application;

public sealed class RuntimeConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<RuntimeConfiguration> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            return RuntimeConfiguration.Unconfigured;
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<RuntimeConfiguration>(stream, JsonOptions, cancellationToken)
            ?? RuntimeConfiguration.Unconfigured;
    }

    public async Task SaveAsync(
        string path,
        RuntimeConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var errors = configuration.Validate();
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Fixed .tmp names collide when multiple saves overlap. Use a unique
        // sibling file so each writer can complete its own atomic replacement.
        var temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(
                    stream, configuration, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, fullPath, true);
        }
        finally
        {
            // A canceled/failed save must not leave a stale configuration file.
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
}
