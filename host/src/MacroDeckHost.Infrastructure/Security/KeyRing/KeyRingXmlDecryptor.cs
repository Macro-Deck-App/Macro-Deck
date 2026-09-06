using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.Security.KeyRing;

/// <summary>
/// Resolved by Data Protection from the assembly-qualified name stored in each key file, so this type
/// must keep its name and namespace for as long as any installation holds a key ring written by it. The
/// <see cref="IServiceProvider"/> constructor is the shape Data Protection's activator prefers.
/// </summary>
public sealed class KeyRingXmlDecryptor : IXmlDecryptor
{
	private readonly IKeyRingKekAccessor _kek;

	public KeyRingXmlDecryptor(IServiceProvider services)
		: this(services.GetRequiredService<IKeyRingKekAccessor>())
	{
	}

	public KeyRingXmlDecryptor(IKeyRingKekAccessor kek) => _kek = kek;

	public XElement Decrypt(XElement encryptedElement)
	{
		ArgumentNullException.ThrowIfNull(encryptedElement);

		var element = encryptedElement.Name == KeyRingXmlNames.SecretElement
			? encryptedElement
			: encryptedElement.Element(KeyRingXmlNames.SecretElement) ??
			throw new CryptographicException("The key ring element is not in the expected format.");

		var version = (int?)element.Attribute(KeyRingXmlNames.VersionAttribute);
		if (version != KeyRingXmlNames.Version)
		{
			throw new CryptographicException("The key ring was written by an unsupported format version " +
				$"({version?.ToString(CultureInfo.InvariantCulture) ?? "none"}).");
		}

		var kekId = (string?)element.Attribute(KeyRingXmlNames.KekIdAttribute) ??
			throw new CryptographicException("The key ring element names no key encryption key.");

		var kek = _kek.TryGetKek() ??
			throw new CryptographicException(
				"The key ring is protected but no key encryption key is available to open it.");

		try
		{
			// Naming the expected key before attempting the tag turns "the keystore holds a different
			// key" into a distinguishable failure rather than a generic authentication error.
			var availableKekId = KeyRingKeyDerivation.DeriveKekId(kek);
			if (!string.Equals(availableKekId, kekId, StringComparison.Ordinal))
			{
				throw new CryptographicException(
					$"The key ring was protected with key encryption key {kekId}, but {availableKekId} is available.");
			}

			var sealedValue = new KeyRingSealed(Read(element, KeyRingXmlNames.NonceElement),
				Read(element, KeyRingXmlNames.ValueElement),
				Read(element, KeyRingXmlNames.TagElement));

			var plaintext = KeyRingAead.TryOpen(kek, sealedValue, KeyRingXmlNames.AssociatedData(kekId)) ??
				throw new CryptographicException("The protected key ring element failed authentication.");

			try
			{
				return XElement.Parse(Encoding.UTF8.GetString(plaintext), LoadOptions.PreserveWhitespace);
			}
			finally
			{
				CryptographicOperations.ZeroMemory(plaintext);
			}
		}
		finally
		{
			CryptographicOperations.ZeroMemory(kek);
		}
	}

	private static byte[] Read(XElement element, XName name)
	{
		var value = (string?)element.Element(name) ??
			throw new CryptographicException($"The protected key ring element has no {name.LocalName}.");

		try
		{
			return Convert.FromBase64String(value);
		}
		catch (FormatException e)
		{
			throw new CryptographicException($"The protected key ring element's {name.LocalName} is malformed.", e);
		}
	}
}
