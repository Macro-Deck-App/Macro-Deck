using System.Security.Cryptography;
using MacroDeckHost.Application.Auth;

namespace MacroDeckHost.Infrastructure.Auth;

/// <summary>
/// Used only while the key ring is locked. <see cref="FileSigningKeyProvider"/> regenerates and
/// overwrites <c>keys/auth-signing.key</c> whenever it cannot unprotect it, which during a temporary
/// lock would destroy the real signing key on the way past. This keeps a key in memory for the lifetime
/// of the process instead: sessions cannot survive the lock either way, but the file is left intact.
/// </summary>
public sealed class EphemeralSigningKeyProvider : ISigningKeyProvider
{
	private readonly byte[] _key = RandomNumberGenerator.GetBytes(64);

	public byte[] GetKey() => _key;
}
