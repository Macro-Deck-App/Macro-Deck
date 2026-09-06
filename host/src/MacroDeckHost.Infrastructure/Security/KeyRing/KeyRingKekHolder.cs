using System.Security.Cryptography;
using MacroDeckHost.Infrastructure.Security.KeyRing.Crypto;

namespace MacroDeckHost.Infrastructure.Security.KeyRing;

public interface IKeyRingKekAccessor
{
	bool HasKek { get; }

	string? KekId { get; }

	/// <summary>Returns a copy the caller may wipe, or null when no key is held.</summary>
	byte[]? TryGetKek();
}

/// <summary>
/// Holds the key-encryption key for the process. Mutable because the key can arrive after startup - the
/// first time a recovery key is exported - and the Data Protection provider is built long before that.
/// </summary>
public sealed class KeyRingKekHolder : IKeyRingKekAccessor
{
	private readonly Lock _gate = new();

	private byte[]? _kek;
	private string? _kekId;

	public bool HasKek
	{
		get
		{
			lock (_gate)
			{
				return _kek is not null;
			}
		}
	}

	public string? KekId
	{
		get
		{
			lock (_gate)
			{
				return _kekId;
			}
		}
	}

	public byte[]? TryGetKek()
	{
		lock (_gate)
		{
			return _kek?.ToArray();
		}
	}

	public void Set(ReadOnlySpan<byte> kek)
	{
		var copy = kek.ToArray();
		var id = KeyRingKeyDerivation.DeriveKekId(copy);

		lock (_gate)
		{
			if (_kek is not null)
			{
				CryptographicOperations.ZeroMemory(_kek);
			}

			_kek = copy;
			_kekId = id;
		}
	}
}
