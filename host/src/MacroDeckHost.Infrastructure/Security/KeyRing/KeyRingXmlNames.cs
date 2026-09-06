using System.Xml.Linq;

namespace MacroDeckHost.Infrastructure.Security.KeyRing;

public static class KeyRingXmlNames
{
	/// <summary>
	/// Data Protection writes this type's assembly-qualified name into every key file it encrypts and
	/// resolves it again on read, so the name is an on-disk contract: moving or renaming
	/// <see cref="KeyRingXmlDecryptor"/> makes every existing installation's key ring unreadable. The
	/// assembly version is pinned in the csproj for the same reason.
	/// </summary>
	public const string DecryptorTypeName = "MacroDeckHost.Infrastructure.Security.KeyRing.KeyRingXmlDecryptor";

	public const string Namespace = "urn:macrodeck:keyring:1";

	public const int Version = 1;

	/// <summary>The wrapper Data Protection itself looks for when deciding a key needs decrypting.</summary>
	public static readonly XNamespace DataProtectionNamespace = "http://schemas.asp.net/2015/03/dataProtection";

	public static readonly XName EncryptedSecretElement = DataProtectionNamespace + "encryptedSecret";

	public static readonly XName RequiresEncryptionAttribute = DataProtectionNamespace + "requiresEncryption";

	public const string DecryptorTypeAttribute = "decryptorType";

	public static readonly XNamespace SecretNamespace = Namespace;

	public static readonly XName SecretElement = SecretNamespace + "protectedKeyRingSecret";

	public static readonly XName NonceElement = SecretNamespace + "nonce";

	public static readonly XName ValueElement = SecretNamespace + "value";

	public static readonly XName TagElement = SecretNamespace + "tag";

	public const string VersionAttribute = "version";

	public const string KekIdAttribute = "kekId";

	public static byte[] AssociatedData(string kekId)
		=> System.Text.Encoding.UTF8.GetBytes($"{Namespace}|{Version}|{kekId}");
}
