using System.Text;

namespace MacroDeckHost.Infrastructure.Usb.Native;

// Throttles and rate limits key a bridged connection on this instead of its loopback address. The
// separator of LoginThrottle's "key|username" format never appears in it.
internal static class NativeUsbDeviceKeys
{
	public const string Prefix = "usb:";

	public static string For(string? serial, string fallback)
		=> Prefix + (Sanitize(serial) ?? Sanitize(fallback) ?? "unknown");

	private static string? Sanitize(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		var builder = new StringBuilder(value.Length);
		foreach (var character in value.Trim())
		{
			builder.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or ':'
				? character
				: '_');
		}

		return builder.ToString();
	}
}
