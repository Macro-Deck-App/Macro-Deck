namespace MacroDeckHost.Infrastructure.Backups.Crypto;

public enum BackupDecryptResult
{
	Success,

	/// <summary>
	/// The key wrap did not authenticate. A wrong recovery key, an edited manifest and an edited
	/// wrapped key are indistinguishable from the key alone, so all three report as a wrong key.
	/// </summary>
	WrongKey,

	Corrupt,
	UnsupportedAlgorithm,
	Truncated,
	TooLarge
}

public sealed class BackupCryptoException : Exception
{
	public BackupCryptoException(BackupDecryptResult result, string message)
		: base(message)
		=> Result = result;

	public BackupDecryptResult Result { get; }
}
