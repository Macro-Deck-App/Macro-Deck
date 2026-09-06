namespace MacroDeckHost.Application.Portable;

public sealed class PortableEncryptionInfo
{
	public const string Aes256Gcm = "AES-256-GCM";

	public string? Cipher { get; set; } = Aes256Gcm;

	public PortableKdfInfo? Kdf { get; set; }
}
