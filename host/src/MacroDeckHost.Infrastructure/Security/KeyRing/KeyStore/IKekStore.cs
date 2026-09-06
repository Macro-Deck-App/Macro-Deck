using MacroDeckHost.Application.Security.KeyRing;

namespace MacroDeckHost.Infrastructure.Security.KeyRing.KeyStore;

/// <summary>
/// The outcome of a keystore operation. <see cref="NotFound"/> and <see cref="Unavailable"/> must never
/// be conflated: the first means the entry can be recreated from the escrow, the second means the
/// backend could not answer and nothing may be rewritten - treating a locked keychain as "no entry"
/// would mint a second key encryption key and strand every secret the first one protects.
/// </summary>
public enum KekStoreStatus
{
	Found,
	NotFound,
	Unavailable
}

public sealed record KekStoreAvailability(bool Supported, string? Reason);

public readonly record struct KekStoreReadResult(KekStoreStatus Status, byte[]? Kek, string? Reason)
{
	public static KekStoreReadResult Found(byte[] kek) => new(KekStoreStatus.Found, kek, null);

	public static KekStoreReadResult NotFound() => new(KekStoreStatus.NotFound, null, null);

	public static KekStoreReadResult Unavailable(string reason) => new(KekStoreStatus.Unavailable, null, reason);
}

/// <summary>
/// Identifies the keystore entry. The account carries the installation id so that two checkouts, or a
/// portable install beside a packaged one, can never write over each other's entry.
/// </summary>
public sealed record KekStoreIdentity(string Service, string Account)
{
	public const string AccountPrefix = "key-ring-kek";

	public static KekStoreIdentity For(string applicationDisplayName, Guid installationId)
		=> new(applicationDisplayName, $"{AccountPrefix}:{installationId:D}");
}

public interface IKekStore
{
	KeyRingBackend Backend { get; }

	/// <summary>
	/// Probes the backend without mutating it. A backend that is present but cannot answer - a locked
	/// keychain, no D-Bus session - reports unsupported with a reason rather than throwing.
	/// </summary>
	KekStoreAvailability Availability { get; }

	KekStoreReadResult Read(KekStoreIdentity identity);

	/// <summary>Returns <see cref="KekStoreStatus.Found"/> when the key was stored.</summary>
	KekStoreStatus Write(KekStoreIdentity identity, ReadOnlySpan<byte> kek);

	KekStoreStatus Delete(KekStoreIdentity identity);
}
