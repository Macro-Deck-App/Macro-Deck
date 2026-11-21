using System.Globalization;
using System.Net;
using System.Text.Json;

namespace MacroDeckHost.Integrations.Ping.Configuration;

internal sealed record PingTargetSettings(string Host, TimeSpan Interval, TimeSpan Timeout, int FailureThreshold)
{
	public const int DefaultIntervalSeconds = 10;

	public const int MinimumIntervalSeconds = 1;

	public const int MaximumIntervalSeconds = 3600;

	public const int DefaultTimeoutMilliseconds = 1000;

	public const int MinimumTimeoutMilliseconds = 100;

	public const int MaximumTimeoutMilliseconds = 10000;

	public const int DefaultFailureThreshold = 3;

	public const int MinimumFailureThreshold = 1;

	public const int MaximumFailureThreshold = 20;

	public static string? NormalizeHost(string? value)
	{
		var host = value?.Trim() ?? string.Empty;
		if (host.Length > 2 && host[0] == '[' && host[^1] == ']')
		{
			host = host[1..^1];
		}

		if (host.Length == 0)
		{
			return null;
		}

		if (IPAddress.TryParse(host, out _))
		{
			return host;
		}

		return Uri.CheckHostName(host) == UriHostNameType.Dns ? host : null;
	}

	public static int? ReadWholeNumber(object? value)
	{
		var number = value switch
		{
			null => null,
			int whole => whole,
			long whole => whole,
			double real => real,
			decimal exact => (double)exact,
			float single => single,
			JsonElement { ValueKind: JsonValueKind.Number } element => element.GetDouble(),
			JsonElement { ValueKind: JsonValueKind.String } element => Parse(element.GetString()),
			string text => Parse(text),
			_ => null
		};

		return number is { } parsed && double.IsFinite(parsed) && Math.Abs(parsed - Math.Round(parsed)) < 1e-9
			? (int)Math.Round(parsed)
			: null;
	}

	private static double? Parse(string? text)
	{
		return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
			? parsed
			: null;
	}
}
