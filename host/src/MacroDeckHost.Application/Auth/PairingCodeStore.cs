using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Application.Auth;

public record PairingCode(string Code, DateTime ExpiresAt);

public sealed class PairingCodeStore
{
	public const int CodeLength = 6;
	public const int MaxFailures = 5;

	private readonly Lock _lock = new();
	private PairingCode? _current;
	private int _failures;

	public static bool IsPairingCodeShape(string token)
		=> token.Length == CodeLength && token.All(char.IsAsciiDigit);

	public PairingCode Rotate(DateTime now)
	{
		lock (_lock)
		{
			string code;
			do
			{
				code = RandomNumberGenerator.GetInt32(1_000_000).ToString("D6", CultureInfo.InvariantCulture);
			} while (code == _current?.Code);

			_current = new PairingCode(code, now.Add(AuthDefaults.PairingCodeLifetime));
			_failures = 0;
			return _current;
		}
	}

	public PairingCode Current(DateTime now)
	{
		lock (_lock)
		{
			return _current is { } code && code.ExpiresAt > now ? code : Rotate(now);
		}
	}

	// Compare and count under one lock: a burst of parallel guesses must never see the code more than
	// MaxFailures times. Failures clear the code and never mint one; only the desktop mints.
	public bool TryRedeem(string candidate, DateTime now)
	{
		lock (_lock)
		{
			if (_current is not { } code || code.ExpiresAt <= now)
			{
				_current = null;
				return false;
			}

			if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(candidate),
				Encoding.ASCII.GetBytes(code.Code)))
			{
				_current = null;
				return true;
			}

			if (++_failures >= MaxFailures)
			{
				_current = null;
			}

			return false;
		}
	}
}
