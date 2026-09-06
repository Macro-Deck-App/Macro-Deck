using System.Runtime.Versioning;
using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Security.KeyRing;

public sealed record KeyRingMigrationOutcome(int Wrapped, int AlreadyWrapped, int Skipped);

/// <summary>
/// Rewrites existing key files so their secrets sit under the key encryption key. Data Protection wraps
/// keys as it creates them but has no way to re-wrap ones already on disk, and its own
/// <c>EncryptIfNecessary</c> is internal, so the element shape it writes is reproduced here.
/// </summary>
public sealed class KeyRingMigrator
{
	private const string TempSuffix = ".tmp";

	private readonly ILogger _logger;

	public KeyRingMigrator(ILogger logger) => _logger = logger.ForContext<KeyRingMigrator>();

	/// <summary>
	/// Every file is rewritten and verified in memory before any of them is replaced on disk, so a file
	/// that cannot be round-tripped aborts the whole pass rather than leaving a ring half converted.
	/// The caller must already have written the escrow and stored the key: a file replaced here is only
	/// readable through them.
	/// </summary>
	public KeyRingMigrationOutcome Migrate(string keysDirectory, IXmlEncryptor encryptor, IXmlDecryptor decryptor)
	{
		var staged = new List<(string Path, XDocument Document)>();
		var alreadyWrapped = 0;
		var skipped = 0;

		foreach (var state in KeyRingFileInspector.InspectDirectory(keysDirectory))
		{
			switch (state.Protection)
			{
				case KeyRingFileProtection.KeyEncryptionKey:
					alreadyWrapped++;
					continue;
				case KeyRingFileProtection.Foreign or KeyRingFileProtection.Unreadable:
					_logger.Warning("Leaving {Path} alone: it is {Protection}", state.Path, state.Protection);
					skipped++;
					continue;
				case KeyRingFileProtection.WindowsDpapi when !OperatingSystem.IsWindows():
					_logger.Warning("Leaving {Path} alone: it is DPAPI protected but this is not Windows", state.Path);
					skipped++;
					continue;
			}

			var document = XDocument.Load(state.Path, LoadOptions.PreserveWhitespace);
			var original = new XDocument(document);

			if (state.Protection == KeyRingFileProtection.WindowsDpapi)
			{
				if (!TryUnwrapDpapi(document))
				{
					_logger.Warning("Leaving {Path} alone: its DPAPI secret could not be opened", state.Path);
					skipped++;
					continue;
				}

				original = new XDocument(document);
			}

			Wrap(document, encryptor);

			// Proving the replacement opens back to what went in is the only assurance that a file is
			// safe to overwrite. Without it a bug in the wrap silently destroys the ring.
			var verification = new XDocument(document);
			Unwrap(verification, decryptor);
			if (!XNode.DeepEquals(Normalize(verification), Normalize(original)))
			{
				throw new InvalidOperationException(
					$"The protected form of {Path.GetFileName(state.Path)} did not decrypt back to its original.");
			}

			staged.Add((state.Path, document));
		}

		foreach (var (path, document) in staged)
		{
			var temp = path + TempSuffix;
			document.Save(temp, SaveOptions.DisableFormatting);
			File.Move(temp, path, overwrite: true);
		}

		if (staged.Count > 0)
		{
			_logger.Information("Protected {Count} key ring file(s) with the key encryption key", staged.Count);
		}

		return new KeyRingMigrationOutcome(staged.Count, alreadyWrapped, skipped);
	}

	private static void Wrap(XDocument document, IXmlEncryptor encryptor)
	{
		while (true)
		{
			var target = document.Descendants().FirstOrDefault(KeyRingFileInspector.RequiresEncryption);
			if (target is null)
			{
				return;
			}

			var encrypted = encryptor.Encrypt(new XElement(target));
			target.ReplaceWith(new XElement(KeyRingXmlNames.EncryptedSecretElement,
				new XAttribute(KeyRingXmlNames.DecryptorTypeAttribute,
					encrypted.DecryptorType.AssemblyQualifiedName!),
				encrypted.EncryptedElement));
		}
	}

	private static void Unwrap(XDocument document, IXmlDecryptor decryptor)
	{
		while (true)
		{
			var target = document.Descendants(KeyRingXmlNames.EncryptedSecretElement).FirstOrDefault();
			if (target is null)
			{
				return;
			}

			// Data Protection hands the decryptor the single child of encryptedSecret, not the wrapper.
			target.ReplaceWith(decryptor.Decrypt(new XElement(target).Elements().Single()));
		}
	}

	private static bool TryUnwrapDpapi(XDocument document)
	{
		if (!OperatingSystem.IsWindows())
		{
			return false;
		}

		try
		{
			UnwrapDpapiCore(document);

			return true;
		}
		catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or InvalidOperationException)
		{
			return false;
		}
	}

	[SupportedOSPlatform("windows")]
	private static void UnwrapDpapiCore(XDocument document) => Unwrap(document, new DpapiXmlDecryptor());

	// Whitespace differs between a parsed file and a freshly built element tree, and says nothing about
	// whether the secrets survived.
	private static XDocument Normalize(XDocument document)
	{
		var copy = new XDocument(document);
		foreach (var text in copy.DescendantNodes().OfType<XText>().Where(t => t.Value.Trim().Length == 0).ToList())
		{
			text.Remove();
		}

		return copy;
	}
}
