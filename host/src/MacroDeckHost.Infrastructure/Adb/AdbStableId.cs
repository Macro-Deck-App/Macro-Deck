using System.Security.Cryptography;
using System.Text;

namespace MacroDeckHost.Infrastructure.Adb;

internal static class AdbStableId
{
	private const int HexLength = 16;

	// Keyed with a per-install secret so the id cannot be matched against a serial list or across installs.
	public static string Compute(byte[] key, string? hardwareSerial, string adbSerial)
	{
		var source = string.IsNullOrWhiteSpace(hardwareSerial)
			|| hardwareSerial.Equals("unknown", StringComparison.OrdinalIgnoreCase)
				? adbSerial
				: hardwareSerial;
		var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(source));
		return Convert.ToHexStringLower(hash)[..HexLength];
	}
}
