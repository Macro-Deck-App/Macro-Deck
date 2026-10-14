using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Application.Auth;

public static class TokenHasher
{
	public const int SecretByteLength = 32;

	public static string Generate()
	{
		return Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretByteLength));
	}

	public static string Hash(string rawToken)
	{
		return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawToken)));
	}

	public static bool Verify(string rawToken, string storedHash)
	{
		return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(Hash(rawToken)),
			Encoding.UTF8.GetBytes(storedHash));
	}
}
