using System.Security.Cryptography;
using MacroDeckHost.Application.Auth;

namespace MacroDeckHost.Infrastructure.Auth;

public class Pbkdf2PasswordHasher : IPasswordHasher
{
	private const string Algorithm = "PBKDF2";
	private const string HashFunction = "SHA256";
	private const int Iterations = 600_000;
	private const int SaltLength = 16;
	private const int SubkeyLength = 32;

	public string Hash(string password)
	{
		var salt = RandomNumberGenerator.GetBytes(SaltLength);
		var subkey = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, SubkeyLength);

		return string.Join('$',
			Algorithm,
			HashFunction,
			Iterations,
			Convert.ToBase64String(salt),
			Convert.ToBase64String(subkey));
	}

	public bool Verify(string password, string encodedHash)
	{
		var parts = encodedHash.Split('$');
		if (parts.Length != 5 || parts[0] != Algorithm || parts[1] != HashFunction)
		{
			return false;
		}

		if (!int.TryParse(parts[2], out var iterations) || iterations < 1)
		{
			return false;
		}

		byte[] salt;
		byte[] expectedSubkey;
		try
		{
			salt = Convert.FromBase64String(parts[3]);
			expectedSubkey = Convert.FromBase64String(parts[4]);
		}
		catch (FormatException)
		{
			return false;
		}

		var actualSubkey = Rfc2898DeriveBytes.Pbkdf2(password,
			salt,
			iterations,
			HashAlgorithmName.SHA256,
			expectedSubkey.Length);

		return CryptographicOperations.FixedTimeEquals(actualSubkey, expectedSubkey);
	}
}
