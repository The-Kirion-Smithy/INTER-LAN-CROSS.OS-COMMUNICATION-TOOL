using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace InterLan.Infrastructure;

public sealed record ServerCertificateDescriptor(
    X509Certificate2 Certificate,
    string Sha256Fingerprint,
    string CertificatePath,
    string PrivateKeyPath);

public static class ServerCertificateManager
{
    public static ServerCertificateDescriptor LoadOrCreate(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var certificatePath = Path.Combine(dataDirectory, "server-cert.pem");
        var privateKeyPath = Path.Combine(dataDirectory, "server-key.pem");

        var certificateExists = File.Exists(certificatePath);
        var privateKeyExists = File.Exists(privateKeyPath);

        // A partially missing pair is an operator recovery incident, not first run.
        // Silently creating a new pair would rotate the server identity and break
        // certificate-pinned clients without the owner's explicit decision.
        if (certificateExists != privateKeyExists)
        {
            throw new InvalidOperationException(
                "Incomplete INTER-LAN server TLS identity: the certificate and " +
                "private key must be restored together from a trusted backup. " +
                "Server startup was stopped; no replacement key was generated.");
        }

        if (!certificateExists)
        {
            Create(certificatePath, privateKeyPath);
        }

        using var pemCertificate = X509Certificate2.CreateFromPemFile(certificatePath, privateKeyPath);

        // Rehydrate through PKCS#12 so Windows Schannel/Kestrel gets a certificate
        // whose private key is backed in a form it can actually use for TLS handshakes.
        // EphemeralKeySet is intentionally avoided because macOS rejected that mode.
        var pfxBytes = pemCertificate.Export(X509ContentType.Pkcs12);
        var certificate = X509CertificateLoader.LoadPkcs12(
            pfxBytes,
            password: null,
            keyStorageFlags: X509KeyStorageFlags.Exportable);

        if (!certificate.HasPrivateKey)
        {
            certificate.Dispose();
            throw new InvalidOperationException("Loaded INTER-LAN server certificate has no private key.");
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(certificate.RawData)).ToLowerInvariant();
        return new ServerCertificateDescriptor(certificate, fingerprint, certificatePath, privateKeyPath);
    }

    private static void Create(string certificatePath, string privateKeyPath)
    {
        using var rsa = RSA.Create(3072);
        var request = new CertificateRequest(
            $"CN=INTER-LAN-{Environment.MachineName}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        request.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddDnsName(Environment.MachineName);
        san.AddIpAddress(IPAddress.Loopback);
        san.AddIpAddress(IPAddress.IPv6Loopback);

        foreach (var address in GetLocalAddresses())
        {
            san.AddIpAddress(address);
        }

        request.CertificateExtensions.Add(san.Build());

        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(5));

        File.WriteAllText(certificatePath, certificate.ExportCertificatePem());
        File.WriteAllText(privateKeyPath, rsa.ExportPkcs8PrivateKeyPem());

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                privateKeyPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static IEnumerable<IPAddress> GetLocalAddresses()
    {
        var addresses = new HashSet<IPAddress>();

        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up ||
                network.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            foreach (var unicast in network.GetIPProperties().UnicastAddresses)
            {
                var address = unicast.Address;
                if (address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 &&
                    !IPAddress.IsLoopback(address) &&
                    !address.IsIPv6LinkLocal)
                {
                    addresses.Add(address);
                }
            }
        }

        return addresses;
    }
}
