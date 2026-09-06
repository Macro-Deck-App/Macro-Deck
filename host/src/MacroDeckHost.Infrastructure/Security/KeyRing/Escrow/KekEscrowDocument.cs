namespace MacroDeckHost.Infrastructure.Security.KeyRing.Escrow;

public sealed class KekEscrowWrap
{
	/// <summary>
	/// <c>BackupKeyDerivation.DeriveKeyId</c> of the recovery key that opens this wrap - the same id the
	/// backup settings screen shows. It is what lets a superseded wrap be pruned without opening it.
	/// </summary>
	public string RecoveryKeyId { get; set; } = string.Empty;

	public DateTimeOffset CreatedAt { get; set; }

	public string Salt { get; set; } = string.Empty;

	public string Nonce { get; set; } = string.Empty;

	public string Ciphertext { get; set; } = string.Empty;

	public string Tag { get; set; } = string.Empty;
}

/// <summary>
/// The key encryption key, wrapped once per recovery key that should still be able to recover it. More
/// than one wrap exists between regenerating a recovery key and exporting the replacement, so that
/// neither the old nor the new key can leave the installation unrecoverable.
/// </summary>
public sealed class KekEscrowDocument
{
	public const int CurrentVersion = 1;

	public int Version { get; set; } = CurrentVersion;

	public string KekId { get; set; } = string.Empty;

	public DateTimeOffset CreatedAt { get; set; }

	public List<KekEscrowWrap> Wraps { get; set; } = [];
}
