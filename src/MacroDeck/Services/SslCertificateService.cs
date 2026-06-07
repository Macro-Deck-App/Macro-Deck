using System.Security.Cryptography.X509Certificates;
using Serilog;
using SuchByte.MacroDeck.StartupConfig;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.Services;

public static class SslCertificateService
{
    private static readonly ILogger Logger = Log.ForContext(typeof(SslCertificateService));

    public static void EnsureValidCertificate()
    {
        if (!MacroDeck.Configuration.EnableSsl)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(MacroDeck.Configuration.SslCertificatePem) &&
            !string.IsNullOrWhiteSpace(MacroDeck.Configuration.SslCertificateKeyPemEncrypted))
        {
            return;
        }

        Logger.Information("No SSL certificate configured – generating self-signed certificate");
        var (certPem, keyPem) = SelfSignedCertificateGenerator.Generate();
        SaveCertificate(certPem, keyPem);
        Logger.Information("Self-signed certificate generated and saved");
    }

    public static X509Certificate2? GetX509Certificate()
    {
        var certPem = MacroDeck.Configuration.SslCertificatePem;
        var keyEncrypted = MacroDeck.Configuration.SslCertificateKeyPemEncrypted;

        if (string.IsNullOrWhiteSpace(certPem) || string.IsNullOrWhiteSpace(keyEncrypted))
        {
            return null;
        }

        try
        {
            var keyPem = StringCipher.Decrypt(keyEncrypted, StringCipher.GetMachineGuid());
            var cert = X509Certificate2.CreateFromPem(certPem, keyPem);
            var pfxBytes = cert.Export(X509ContentType.Pfx);
            cert.Dispose();
            return X509CertificateLoader.LoadPkcs12(pfxBytes, null);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed to load SSL certificate");
            return null;
        }
    }

    public static bool TryValidate(string certPem, string keyPem, out string? error)
    {
        if (string.IsNullOrWhiteSpace(certPem))
        {
            error = "Certificate PEM is empty.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(keyPem))
        {
            error = "Private key PEM is empty.";
            return false;
        }

        try
        {
            using var cert = X509Certificate2.CreateFromPem(certPem, keyPem);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static void SaveCertificate(string certPem, string keyPem)
    {
        var keyEncrypted = StringCipher.Encrypt(keyPem, StringCipher.GetMachineGuid());
        MacroDeck.Configuration.SslCertificatePem = certPem;
        MacroDeck.Configuration.SslCertificateKeyPemEncrypted = keyEncrypted;
        MacroDeck.Configuration.Save(ApplicationPaths.MainConfigFilePath);
    }
}
