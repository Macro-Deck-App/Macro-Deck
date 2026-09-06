using System.Xml.Linq;

namespace MacroDeckHost.Infrastructure.Security.KeyRing;

public enum KeyRingFileProtection
{
	/// <summary>Key material sits in the file in the clear - what macOS and Linux have today.</summary>
	Plaintext,

	/// <summary>Wrapped by the framework's DPAPI encryptor - what an existing Windows install has.</summary>
	WindowsDpapi,

	/// <summary>Wrapped by this feature's encryptor, under the named key encryption key.</summary>
	KeyEncryptionKey,

	/// <summary>Wrapped by something Macro Deck never configures, so it is left strictly alone.</summary>
	Foreign,

	Unreadable
}

public sealed record KeyRingFileState(string Path, KeyRingFileProtection Protection, string? KekId);

/// <summary>
/// Classifies key files by reading their XML only. Nothing here decrypts, so it is safe to run before
/// the key encryption key is known - which is exactly when the host has to decide what state it is in.
/// </summary>
public static class KeyRingFileInspector
{
	public const string KeyFilePattern = "key-*.xml";

	public static IReadOnlyList<KeyRingFileState> InspectDirectory(string keysDirectory)
	{
		if (!Directory.Exists(keysDirectory))
		{
			return [];
		}

		return
		[
			.. Directory.EnumerateFiles(keysDirectory, KeyFilePattern).Order(StringComparer.Ordinal).Select(InspectFile)
		];
	}

	public static KeyRingFileState InspectFile(string path)
	{
		XDocument document;
		try
		{
			document = XDocument.Load(path, LoadOptions.PreserveWhitespace);
		}
		catch (Exception e) when (e is System.Xml.XmlException or IOException or UnauthorizedAccessException)
		{
			return new KeyRingFileState(path, KeyRingFileProtection.Unreadable, null);
		}

		var encrypted = document.Descendants(KeyRingXmlNames.EncryptedSecretElement).ToList();
		if (encrypted.Count == 0)
		{
			return new KeyRingFileState(path,
				document.Descendants().Any(RequiresEncryption)
					? KeyRingFileProtection.Plaintext
					: KeyRingFileProtection.Foreign,
				null);
		}

		foreach (var element in encrypted)
		{
			var decryptorType = (string?)element.Attribute(KeyRingXmlNames.DecryptorTypeAttribute) ?? string.Empty;

			if (decryptorType.StartsWith(KeyRingXmlNames.DecryptorTypeName, StringComparison.Ordinal))
			{
				var kekId = (string?)element.Element(KeyRingXmlNames.SecretElement)
					?.Attribute(KeyRingXmlNames.KekIdAttribute);

				return new KeyRingFileState(path, KeyRingFileProtection.KeyEncryptionKey, kekId);
			}

			if (decryptorType.Contains("DpapiXmlDecryptor", StringComparison.Ordinal))
			{
				return new KeyRingFileState(path, KeyRingFileProtection.WindowsDpapi, null);
			}
		}

		return new KeyRingFileState(path, KeyRingFileProtection.Foreign, null);
	}

	public static bool RequiresEncryption(XElement element)
		=> string.Equals((string?)element.Attribute(KeyRingXmlNames.RequiresEncryptionAttribute),
			"true",
			StringComparison.OrdinalIgnoreCase);
}
