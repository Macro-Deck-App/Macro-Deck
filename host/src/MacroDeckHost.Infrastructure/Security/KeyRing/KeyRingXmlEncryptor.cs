using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;

namespace MacroDeckHost.Infrastructure.Security.KeyRing;

public sealed class KeyRingXmlEncryptor : IXmlEncryptor
{
	private readonly IKeyRingKekAccessor _kek;

	public KeyRingXmlEncryptor(IKeyRingKekAccessor kek) => _kek = kek;

	public EncryptedXmlInfo Encrypt(XElement plaintextElement)
	{
		ArgumentNullException.ThrowIfNull(plaintextElement);

		var kek = _kek.TryGetKek() ??
			throw new InvalidOperationException(
				"The key ring cannot be encrypted because no key encryption key is available.");

		try
		{
			var kekId = KeyRingKeyDerivation.DeriveKekId(kek);

			// ToString rather than mutating the caller's element: IXmlEncryptor is contractually not
			// allowed to touch what it is handed.
			var plaintext = Encoding.UTF8.GetBytes(plaintextElement.ToString(SaveOptions.DisableFormatting));
			var sealedValue = KeyRingAead.Seal(kek, plaintext, KeyRingXmlNames.AssociatedData(kekId));
			CryptographicOperations.ZeroMemory(plaintext);

			var element = new XElement(KeyRingXmlNames.SecretElement,
				new XAttribute(KeyRingXmlNames.VersionAttribute, KeyRingXmlNames.Version),
				new XAttribute(KeyRingXmlNames.KekIdAttribute, kekId),
				new XElement(KeyRingXmlNames.NonceElement, Convert.ToBase64String(sealedValue.Nonce)),
				new XElement(KeyRingXmlNames.ValueElement, Convert.ToBase64String(sealedValue.Ciphertext)),
				new XElement(KeyRingXmlNames.TagElement, Convert.ToBase64String(sealedValue.Tag)));

			return new EncryptedXmlInfo(element, typeof(KeyRingXmlDecryptor));
		}
		finally
		{
			CryptographicOperations.ZeroMemory(kek);
		}
	}
}
