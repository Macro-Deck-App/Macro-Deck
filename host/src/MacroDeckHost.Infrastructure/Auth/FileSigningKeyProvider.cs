using System.Security.Cryptography;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Paths;
using Microsoft.AspNetCore.DataProtection;
using Serilog;

namespace MacroDeckHost.Infrastructure.Auth;

public class FileSigningKeyProvider : ISigningKeyProvider
{
	private const string KeyFileName = "auth-signing.key";
	private const int KeyLength = 64;

	private readonly Lazy<byte[]> _key;
	private readonly IDataProtector _protector;
	private readonly string _keyFilePath;
	private readonly ILogger _logger;

	public FileSigningKeyProvider(IDataProtectionProvider dataProtectionProvider, IMacroDeckPaths paths, ILogger logger)
	{
		_protector = dataProtectionProvider.CreateProtector("MacroDeck.Auth.SigningKey");
		_keyFilePath = Path.Combine(paths.KeysDirectory, KeyFileName);
		_logger = logger;
		_key = new Lazy<byte[]>(LoadOrCreateKey, LazyThreadSafetyMode.ExecutionAndPublication);
	}

	public byte[] GetKey() => _key.Value;

	private byte[] LoadOrCreateKey()
	{
		if (File.Exists(_keyFilePath))
		{
			try
			{
				return _protector.Unprotect(File.ReadAllBytes(_keyFilePath));
			}
			catch (CryptographicException ex)
			{
				_logger.Warning(ex,
					"Failed to unprotect the auth signing key; generating a new one. All sessions are invalidated");
			}
		}

		var key = RandomNumberGenerator.GetBytes(KeyLength);
		Directory.CreateDirectory(Path.GetDirectoryName(_keyFilePath)!);
		File.WriteAllBytes(_keyFilePath, _protector.Protect(key));

		return key;
	}
}
