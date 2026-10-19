using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace InterLan.Application;

public sealed record ClientPairingState(
    Guid ServerId,
    string ServerUri,
    string PinnedCertificateSha256,
    Guid DeviceId,
    string DeviceCredential,
    string DeviceName,
    DateTimeOffset PairedUtc)
{
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (ServerId == Guid.Empty) errors.Add("ServerId is required.");
        if (DeviceId == Guid.Empty) errors.Add("DeviceId is required.");
        if (!Uri.TryCreate(ServerUri, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("ServerUri must be an absolute HTTPS URI.");
        }
        else if (!string.IsNullOrEmpty(uri.UserInfo) ||
                 !string.IsNullOrEmpty(uri.Query) ||
                 !string.IsNullOrEmpty(uri.Fragment) ||
                 uri.AbsolutePath != "/")
        {
            // A pinned owner endpoint is an origin, never a URL containing
            // embedded credentials or caller-controlled request components.
            errors.Add("ServerUri must be an HTTPS owner origin without user info, path, query or fragment.");
        }
        if (string.IsNullOrEmpty(PinnedCertificateSha256) ||
            PinnedCertificateSha256.Length != 64 ||
            !PinnedCertificateSha256.All(Uri.IsHexDigit))
            errors.Add("PinnedCertificateSha256 must be 64 hexadecimal characters.");
        if (string.IsNullOrWhiteSpace(DeviceCredential))
            errors.Add("DeviceCredential is required.");
        if (string.IsNullOrWhiteSpace(DeviceName))
            errors.Add("DeviceName is required.");

        return errors;
    }
}

public sealed class ClientPairingStateStore
{
    private const byte FormatVersion = 1;
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static string GetDefaultPath()
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(root))
            root = AppContext.BaseDirectory;

        return Path.Combine(root, "InterLan", "pairing.state");
    }

    public async Task SaveAsync(
        string path,
        ClientPairingState state,
        CancellationToken cancellationToken = default)
    {
        var errors = state.Validate();
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var key = await GetOrCreateKeyAsync(fullPath, cancellationToken);
        byte[]? plaintext = null;
        try
        {
            plaintext = JsonSerializer.SerializeToUtf8Bytes(state);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var ciphertext = new byte[plaintext.Length];
            var tag = new byte[TagSize];

            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Encrypt(nonce, plaintext, ciphertext, tag);
            }

            var payload = new byte[1 + NonceSize + TagSize + ciphertext.Length];
            payload[0] = FormatVersion;
            nonce.CopyTo(payload.AsSpan(1, NonceSize));
            tag.CopyTo(payload.AsSpan(1 + NonceSize, TagSize));
            ciphertext.CopyTo(payload.AsSpan(1 + NonceSize + TagSize));

            await AtomicWriteAsync(fullPath, payload, cancellationToken);
            RestrictUnixPermissions(fullPath);
        }
        finally
        {
            // Cancellation, I/O errors and encryption failures must not leave
            // the pairing credential or AES key in an abandoned managed buffer.
            CryptographicOperations.ZeroMemory(key);
            if (plaintext is not null)
                CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public async Task<ClientPairingState?> LoadAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            return null;

        var payload = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        if (payload.Length < 1 + NonceSize + TagSize || payload[0] != FormatVersion)
            throw new InvalidDataException("Client pairing state format is invalid.");

        var key = await ReadExistingKeyAsync(fullPath, cancellationToken);
        var nonce = payload.AsSpan(1, NonceSize);
        var tag = payload.AsSpan(1 + NonceSize, TagSize);
        var ciphertext = payload.AsSpan(1 + NonceSize + TagSize);
        var plaintext = new byte[ciphertext.Length];

        try
        {
            using (var aes = new AesGcm(key, TagSize))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            var state = JsonSerializer.Deserialize<ClientPairingState>(plaintext)
                ?? throw new InvalidDataException("Client pairing state is empty.");

            var errors = state.Validate();
            if (errors.Count > 0)
                throw new InvalidDataException(string.Join(Environment.NewLine, errors));

            return state;
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("Client pairing state could not be decrypted.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    public void Delete(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (File.Exists(fullPath)) File.Delete(fullPath);

        var keyPath = KeyPath(fullPath);
        if (File.Exists(keyPath)) File.Delete(keyPath);
    }

    private static async Task<byte[]> GetOrCreateKeyAsync(
        string statePath,
        CancellationToken cancellationToken)
    {
        var keyPath = KeyPath(statePath);
        if (File.Exists(keyPath))
            return await ReadExistingKeyAsync(statePath, cancellationToken);

        // Existing ciphertext without its original encryption key is an
        // incomplete backup/restore incident, not a new pairing.
        if (File.Exists(statePath))
            throw new InvalidDataException(
                "Client pairing key is missing. Restore its matching key from backup or re-enroll.");

        var created = RandomNumberGenerator.GetBytes(KeySize);
        byte[]? persistedKey = null;
        try
        {
            persistedKey = OperatingSystem.IsWindows()
                ? WindowsDataProtection.Protect(created)
                : created.ToArray();

            await AtomicWriteAsync(keyPath, persistedKey, cancellationToken);
            RestrictUnixPermissions(keyPath);
            return created;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(created);
            throw;
        }
        finally
        {
            if (persistedKey is not null)
                CryptographicOperations.ZeroMemory(persistedKey);
        }
    }

    private static async Task<byte[]> ReadExistingKeyAsync(
        string statePath,
        CancellationToken cancellationToken)
    {
        var keyPath = KeyPath(statePath);
        if (!File.Exists(keyPath))
            throw new InvalidDataException(
                "Client pairing key is missing. Restore its matching key from backup or re-enroll.");

        var persisted = await File.ReadAllBytesAsync(keyPath, cancellationToken);
        try
        {
            // Avoid aliasing the persisted buffer before it is scrubbed.
            // Protected platform key vault support is tracked in #40.
            var key = OperatingSystem.IsWindows()
                ? WindowsDataProtection.Unprotect(persisted)
                : persisted.ToArray();

            if (key.Length != KeySize)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new InvalidDataException("Client pairing key is invalid.");
            }

            return key;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(persisted);
        }
    }

    private static string KeyPath(string statePath) => statePath + ".key";

    private static async Task AtomicWriteAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        // Each writer owns its staging file, so overlapping saves cannot
        // truncate each other's ciphertext or protected key material.
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous
        };

        // Never create a secret-bearing staging file as world-readable,
        // even momentarily before the final rename/chmod.
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        try
        {
            await using (var stream = new FileStream(temp, options))
            {
                await stream.WriteAsync(bytes.AsMemory(), cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temp, path, true);
        }
        finally
        {
            // Cancellation and failed replacements cannot leave a predictable
            // orphan containing an AES key or pairing ciphertext.
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static void RestrictUnixPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
            return;

        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static class WindowsDataProtection
    {
        private const uint CryptProtectUiForbidden = 0x1;

        [StructLayout(LayoutKind.Sequential)]
        private struct DataBlob
        {
            public int Size;
            public IntPtr Data;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptProtectData(
            ref DataBlob dataIn,
            string? description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            uint flags,
            out DataBlob dataOut);

        [DllImport("crypt32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CryptUnprotectData(
            ref DataBlob dataIn,
            IntPtr description,
            IntPtr optionalEntropy,
            IntPtr reserved,
            IntPtr prompt,
            uint flags,
            out DataBlob dataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr memory);

        public static byte[] Protect(byte[] value) =>
            Transform(value, protect: true);

        public static byte[] Unprotect(byte[] value) =>
            Transform(value, protect: false);

        private static byte[] Transform(byte[] value, bool protect)
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException();

            var input = new DataBlob
            {
                Size = value.Length,
                Data = Marshal.AllocHGlobal(value.Length)
            };
            try
            {
                Marshal.Copy(value, 0, input.Data, value.Length);
                DataBlob output;
                var success = protect
                    ? CryptProtectData(
                        ref input,
                        "INTER-LAN client pairing key",
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptProtectUiForbidden,
                        out output)
                    : CryptUnprotectData(
                        ref input,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        CryptProtectUiForbidden,
                        out output);

                if (!success)
                    throw new CryptographicException(Marshal.GetLastWin32Error());

                try
                {
                    var result = new byte[output.Size];
                    Marshal.Copy(output.Data, result, 0, output.Size);
                    return result;
                }
                finally
                {
                    if (output.Data != IntPtr.Zero)
                    {
                        try { ClearNativeBuffer(output.Data, output.Size); }
                        finally { LocalFree(output.Data); }
                    }
                }
            }
            finally
            {
                try { ClearNativeBuffer(input.Data, input.Size); }
                finally { Marshal.FreeHGlobal(input.Data); }
            }
        }

        private static void ClearNativeBuffer(IntPtr buffer, int length)
        {
            if (buffer == IntPtr.Zero || length <= 0)
                return;

            // DPAPI unprotect returns the plaintext AES key in unmanaged memory.
            // Explicitly overwrite that allocation before returning it to the heap.
            var zeros = new byte[Math.Min(length, 4096)];
            for (var offset = 0; offset < length; offset += zeros.Length)
            {
                Marshal.Copy(
                    zeros, 0, IntPtr.Add(buffer, offset),
                    Math.Min(zeros.Length, length - offset));
            }
        }
    }
}
