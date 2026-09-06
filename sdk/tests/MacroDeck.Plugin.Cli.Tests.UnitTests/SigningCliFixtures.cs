using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using MacroDeck.Signing;
using MacroDeck.Signing.Certificates;
using MacroDeck.Signing.Keys;
using NSec.Cryptography;

namespace MacroDeck.Plugin.Cli.Tests.UnitTests;

/// <summary>
/// A throwaway root key pair and certificate-issuing helper for <c>sign</c>/<c>verify</c> tests, built
/// exactly like <c>TestPki</c> in <c>MacroDeck.Signing.Tests.UnitTests</c> (the two cannot share code across
/// projects, but the shape is identical): the real Macro Deck root key exists only in the offline
/// key-generation tool, so every CLI test signs against a root of its own and verifies with
/// <c>--root-public</c> pointed at it. That is a deliberate, unavoidable substitution - see the scenario
/// notes in the test files for what it means every test here always sees the <c>non-production-root</c>
/// warning and <c>rootAnchored: false</c>.
/// </summary>
internal static class SigningCliFixtures
{
	public static readonly (byte[] PrivateKey, byte[] PublicKey) Root = Ed25519KeyPair.Create();

	public sealed record IssuedCertificate(
		byte[] CertificateBytes,
		byte[] CertificateSignatureBytes,
		byte[] PrivateKey,
		byte[] PublicKey,
		string CertificateId);

	public static IssuedCertificate IssueCertificate(
		string subjectKind = "creator",
		IReadOnlyList<string>? keyUsage = null,
		DateTimeOffset? notBefore = null,
		DateTimeOffset? notAfter = null)
	{
		var subjectKeys = Ed25519KeyPair.Create();
		var certificateId = "cert_" + Guid.NewGuid().ToString("N");

		var certificate = new SigningCertificate
		{
			SchemaVersion = 1,
			Subject = new SigningCertificateSubject { Kind = subjectKind, Id = "creator-1", Name = "Test Creator" },
			Algorithm = "ed25519",
			CertificateId = certificateId,
			PublicKey = Convert.ToBase64String(subjectKeys.PublicKey),
			KeyUsage = keyUsage ?? [SigningCertificateChain.PackageKeyUsage],
			NotBefore = notBefore ?? DateTimeOffset.UtcNow.AddDays(-1),
			NotAfter = notAfter ?? DateTimeOffset.UtcNow.AddYears(1),
			IssuedAt = notBefore ?? DateTimeOffset.UtcNow.AddDays(-1),
			RootKeyId = "root_test0001"
		};

		var certificateBytes = SigningJson.Serialize(certificate);
		var certificateSignatureBytes = SignAsBase64Text(Root.PrivateKey, certificateBytes);

		return new IssuedCertificate(certificateBytes,
			certificateSignatureBytes,
			subjectKeys.PrivateKey,
			subjectKeys.PublicKey,
			certificateId);
	}

	/// <summary>Writes <paramref name="issued" />'s certificate, certificate signature and base64 private
	/// key to <paramref name="directory" />, returning the three paths <c>sign</c> takes.</summary>
	public static (string CertificatePath, string CertificateSignaturePath, string PrivateKeyPath)
		WriteCertificateFiles(
			string directory,
			IssuedCertificate issued,
			string baseName = "creator")
	{
		var certificatePath = Path.Combine(directory, $"{baseName}.certificate.json");
		var certificateSignaturePath = Path.Combine(directory, $"{baseName}.certificate.sig");
		var privateKeyPath = Path.Combine(directory, $"{baseName}.private");

		File.WriteAllBytes(certificatePath, issued.CertificateBytes);
		File.WriteAllBytes(certificateSignaturePath, issued.CertificateSignatureBytes);
		File.WriteAllText(privateKeyPath, Convert.ToBase64String(issued.PrivateKey) + "\n");

		return (certificatePath, certificateSignaturePath, privateKeyPath);
	}

	public static string WriteRootPublicKeyFile(string directory,
		byte[]? publicKey = null,
		string fileName = "root.public")
	{
		var path = Path.Combine(directory, fileName);
		File.WriteAllText(path, Convert.ToBase64String(publicKey ?? Root.PublicKey) + "\n");
		return path;
	}

	public sealed record DeclaredFile(string Path, string Content);

	/// <summary>Writes an unsigned <c>.macroDeckPlugin</c> archive, hand-built with
	/// <see cref="ZipArchive" /> - never through <c>PluginPacker</c> - so a CLI test built from one can never
	/// merely confirm the CLI agrees with the rest of the codebase.</summary>
	public static string CreatePluginArchive(string path,
		string id = "com.example.test",
		string version = "1.0.0",
		IReadOnlyList<DeclaredFile>? files = null)
	{
		files ??= [new DeclaredFile("app", "binary-content")];

		var manifest = new JsonObject
		{
			["manifestVersion"] = 1,
			["id"] = id,
			["name"] = "Test Plugin",
			["version"] = version,
			["entrypoints"] = new JsonObject { ["linux-x64"] = new JsonObject { ["executable"] = "app" } },
			["files"] = new JsonArray(files.Select(file => (JsonNode)new JsonObject
			{
				["path"] = file.Path,
				["sha256"] = "sha256:" + Sha256Hex(file.Content),
				["size"] = Encoding.UTF8.GetByteCount(file.Content)
			}).ToArray())
		};

		using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
		WriteEntry(archive, "manifest.json", manifest.ToJsonString());
		foreach (var file in files)
		{
			WriteEntry(archive, file.Path, file.Content);
		}

		return path;
	}

	public static void ReplaceEntry(string archivePath, string entryName, string newContent)
	{
		using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Update);
		archive.GetEntry(entryName)?.Delete();
		WriteEntry(archive, entryName, newContent);
	}

	public static string ReadEntryText(string archivePath, string entryName)
	{
		using var stream = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
		using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
		var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"No entry '{entryName}'.");
		using var reader = new StreamReader(entry.Open());
		return reader.ReadToEnd();
	}

	private static void WriteEntry(ZipArchive archive, string entryName, string content)
	{
		var entry = archive.CreateEntry(entryName, CompressionLevel.NoCompression);
		using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		writer.Write(content);
	}

	private static string Sha256Hex(string content) =>
		Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

	private static byte[] SignAsBase64Text(byte[] rawPrivateKey, byte[] payload)
	{
		using var key = Key.Import(SignatureAlgorithm.Ed25519, rawPrivateKey, KeyBlobFormat.RawPrivateKey);
		var signature = SignatureAlgorithm.Ed25519.Sign(key, payload);
		return Encoding.UTF8.GetBytes(Convert.ToBase64String(signature));
	}
}
