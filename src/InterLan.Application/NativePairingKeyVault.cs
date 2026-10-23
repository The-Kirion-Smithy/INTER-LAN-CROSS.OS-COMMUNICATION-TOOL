using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace InterLan.Application;

/// <summary>
/// Protects the 256-bit pairing-state encryption key in an OS credential vault.
/// The state path is an identity label only. No AES key is written to a sidecar.
/// </summary>
internal static class NativePairingKeyVault
{
    private const string Service = "InterLan.NativePairingKey.v1";

    public static bool HasPlatformAdapter =>
        OperatingSystem.IsMacOS() ||
        (OperatingSystem.IsLinux() && File.Exists("/usr/bin/secret-tool"));

    public static async Task StoreAsync(string statePath, byte[] key, CancellationToken ct)
    {
        if (key.Length != 32)
            throw new ArgumentException("A 256-bit key is required.", nameof(key));
        if (OperatingSystem.IsMacOS())
        {
            MacKeychain.Store(Account(statePath), key);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            await LinuxSecretService.InvokeAsync(
                "store", Account(statePath), key, ct);
            return;
        }

        throw new PlatformNotSupportedException("No supported native credential vault is configured.");
    }

    public static async Task<byte[]> ReadAsync(string statePath, CancellationToken ct)
    {
        byte[] key;
        if (OperatingSystem.IsMacOS())
            key = MacKeychain.Read(Account(statePath));
        else if (OperatingSystem.IsLinux())
        {
            var encoded = await LinuxSecretService.InvokeAsync(
                "lookup", Account(statePath), null, ct);
            try
            {
                key = Convert.FromBase64String(encoded ??
                    throw new CryptographicException("Native pairing key is missing."));
            }
            catch (FormatException error)
            {
                throw new CryptographicException("Native pairing key has invalid encoding.", error);
            }
        }
        else
            throw new PlatformNotSupportedException("No supported native credential vault is configured.");

        if (key.Length != 32)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new CryptographicException("Native pairing key has an invalid length.");
        }

        return key;
    }

    public static async Task DeleteAsync(string statePath, CancellationToken ct)
    {
        if (OperatingSystem.IsMacOS())
            MacKeychain.Delete(Account(statePath));
        else if (OperatingSystem.IsLinux())
            await LinuxSecretService.InvokeAsync("clear", Account(statePath), null, ct);
        else
            throw new PlatformNotSupportedException("No supported native credential vault is configured.");
    }

    private static string Account(string statePath)
    {
        var normalizedPath = Path.GetFullPath(statePath);
        var bytes = Encoding.UTF8.GetBytes(normalizedPath);
        try { return Convert.ToHexString(SHA256.HashData(bytes)); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    private static class LinuxSecretService
    {
        private const string Executable = "/usr/bin/secret-tool";

        public static async Task<string?> InvokeAsync(
            string operation, string account, byte[]? key, CancellationToken ct)
        {
            if (!File.Exists(Executable))
                throw new PlatformNotSupportedException(
                    "Linux Secret Service adapter requires /usr/bin/secret-tool and a running, unlocked Secret Service.");

            var start = new ProcessStartInfo(Executable)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add(operation);
            if (operation == "store")
                start.ArgumentList.Add("--label=INTER-LAN native pairing key");
            start.ArgumentList.Add("interlan-service");
            start.ArgumentList.Add(Service);
            start.ArgumentList.Add("interlan-account");
            start.ArgumentList.Add(account);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var process = new Process { StartInfo = start };
            var started = false;
            try
            {
                started = process.Start();
                if (!started)
                    throw new InvalidOperationException("Native Secret Service helper could not start.");

                var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var stderr = process.StandardError.ReadToEndAsync(timeout.Token);

                if (key is not null)
                {
                    // No secret in shell command text, process arguments, or logs.
                    // secret-tool consumes the password from redirected stdin.
                    await process.StandardInput.WriteAsync(Convert.ToBase64String(key));
                }
                process.StandardInput.Close();

                await process.WaitForExitAsync(timeout.Token);
                var output = await stdout;
                _ = await stderr; // Deliberately do not surface helper stderr (could contain sensitive data).
                if (process.ExitCode != 0)
                    throw new CryptographicException("Linux Secret Service operation failed or the vault is locked.");

                return operation == "lookup" ? output.TrimEnd('\r', '\n') : null;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new CryptographicException("Linux Secret Service is unavailable or requires an interactive unlock.");
            }
            finally
            {
                if (started)
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill(entireProcessTree: true);
                    }
                    catch (InvalidOperationException)
                    {
                        // Process exited between the state check and kill.
                    }
                }
            }
        }
    }

    private static class MacKeychain
    {
        private const int ItemNotFound = -25300;
        private const int DuplicateItem = -25299;
        private const string SecurityFramework =
            "/System/Library/Frameworks/Security.framework/Security";
        private const string CoreFoundationFramework =
            "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [DllImport(SecurityFramework)]
        private static extern int SecKeychainAddGenericPassword(
            IntPtr keychain, uint serviceLength, byte[] service,
            uint accountLength, byte[] account, uint secretLength,
            byte[] secret, out IntPtr item);

        [DllImport(SecurityFramework)]
        private static extern int SecKeychainFindGenericPassword(
            IntPtr keychainOrArray, uint serviceLength, byte[] service,
            uint accountLength, byte[] account, out uint secretLength,
            out IntPtr secret, out IntPtr item);

        [DllImport(SecurityFramework)]
        private static extern int SecKeychainItemModifyAttributesAndData(
            IntPtr item, IntPtr attributes, uint secretLength, byte[] secret);

        [DllImport(SecurityFramework)]
        private static extern int SecKeychainItemDelete(IntPtr item);

        [DllImport(SecurityFramework)]
        private static extern int SecKeychainItemFreeContent(
            IntPtr attributes, IntPtr data);

        [DllImport(CoreFoundationFramework)]
        private static extern void CFRelease(IntPtr reference);

        public static void Store(string accountName, byte[] key)
        {
            var service = Encoding.UTF8.GetBytes(Service);
            var account = Encoding.UTF8.GetBytes(accountName);
            try
            {
                var status = SecKeychainAddGenericPassword(
                    IntPtr.Zero, (uint)service.Length, service,
                    (uint)account.Length, account, (uint)key.Length, key,
                    out var added);
                if (added != IntPtr.Zero)
                    CFRelease(added);

                if (status == DuplicateItem)
                {
                    var item = Find(service, account, out var secret);
                    try
                    {
                        var result = SecKeychainItemModifyAttributesAndData(
                            item, IntPtr.Zero, (uint)key.Length, key);
                        EnsureSuccess(result);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(secret);
                        CFRelease(item);
                    }
                }
                else
                    EnsureSuccess(status);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(service);
                CryptographicOperations.ZeroMemory(account);
            }
        }

        public static byte[] Read(string accountName)
        {
            var service = Encoding.UTF8.GetBytes(Service);
            var account = Encoding.UTF8.GetBytes(accountName);
            try
            {
                var item = Find(service, account, out var key);
                CFRelease(item);
                return key;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(service);
                CryptographicOperations.ZeroMemory(account);
            }
        }

        public static void Delete(string accountName)
        {
            var service = Encoding.UTF8.GetBytes(Service);
            var account = Encoding.UTF8.GetBytes(accountName);
            try
            {
                var status = SecKeychainFindGenericPassword(
                    IntPtr.Zero, (uint)service.Length, service,
                    (uint)account.Length, account,
                    out var length, out var secret, out var item);
                if (status == ItemNotFound)
                    return;
                EnsureSuccess(status);
                try { EnsureSuccess(SecKeychainItemDelete(item)); }
                finally
                {
                    if (secret != IntPtr.Zero)
                        SecKeychainItemFreeContent(IntPtr.Zero, secret);
                    if (item != IntPtr.Zero)
                        CFRelease(item);
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(service);
                CryptographicOperations.ZeroMemory(account);
            }
        }

        private static IntPtr Find(byte[] service, byte[] account, out byte[] value)
        {
            var status = SecKeychainFindGenericPassword(
                IntPtr.Zero, (uint)service.Length, service,
                (uint)account.Length, account,
                out var length, out var nativeValue, out var item);
            if (status != 0)
            {
                if (item != IntPtr.Zero) CFRelease(item);
                EnsureSuccess(status);
            }
            try
            {
                if (length != 32 || nativeValue == IntPtr.Zero)
                    throw new CryptographicException("macOS Keychain pairing key is absent or invalid.");
                value = new byte[checked((int)length)];
                Marshal.Copy(nativeValue, value, 0, (int)length);
                return item;
            }
            catch
            {
                if (item != IntPtr.Zero) CFRelease(item);
                throw;
            }
            finally
            {
                if (nativeValue != IntPtr.Zero)
                    SecKeychainItemFreeContent(IntPtr.Zero, nativeValue);
            }
        }

        private static void EnsureSuccess(int status)
        {
            if (status != 0)
                throw new CryptographicException(
                    "macOS Keychain operation failed (OSStatus " + status + ").");
        }
    }
}
