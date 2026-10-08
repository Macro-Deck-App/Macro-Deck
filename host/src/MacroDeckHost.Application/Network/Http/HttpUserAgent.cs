using System.Net.Http;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Application.Network.Http;

public static class HttpUserAgent
{
	public const int MaxLength = 256;

	public static string Default => $"MacroDeck/{HostVersion.Current}";

	public static bool TryNormalize(string? value, out string normalized)
	{
		normalized = string.Empty;

		var trimmed = value?.Trim();
		if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLength)
		{
			return false;
		}

		foreach (var character in trimmed)
		{
			if (character is < ' ' or > '~')
			{
				return false;
			}
		}

		using var probe = new HttpRequestMessage();
		if (!probe.Headers.UserAgent.TryParseAdd(trimmed))
		{
			return false;
		}

		normalized = trimmed;
		return true;
	}
}
