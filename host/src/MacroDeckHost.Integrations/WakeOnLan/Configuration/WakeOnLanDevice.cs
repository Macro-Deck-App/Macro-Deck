using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Integrations.WakeOnLan.Configuration;

internal sealed partial record WakeOnLanDevice(
	byte[] Mac,
	IPAddress BroadcastAddress,
	int Port,
	int Packets,
	TimeSpan Delay)
{
	public const string DefaultBroadcastAddress = "255.255.255.255";
	public const int DefaultPort = 9;
	public const int MinPort = 1;
	public const int MaxPort = 65535;
	public const int DefaultPackets = 1;
	public const int MinPackets = 1;
	public const int MaxPackets = 10;
	public const int DefaultDelayMilliseconds = 500;
	public const int MinDelayMilliseconds = 0;
	public const int MaxDelayMilliseconds = 10000;

	public IPEndPoint Target => new(BroadcastAddress, Port);

	public static async Task<WakeOnLanDevice?> ReadAsync(
		IIntegrationConfig config,
		Guid entryId,
		CancellationToken cancellationToken)
	{
		var mac = await config.GetStringAsync(entryId, WakeOnLanConfigKeys.MacAddress, cancellationToken)
			.ConfigureAwait(false);
		var address = await config.GetStringAsync(entryId, WakeOnLanConfigKeys.BroadcastAddress, cancellationToken)
			.ConfigureAwait(false);
		var port = await config.GetStringAsync(entryId, WakeOnLanConfigKeys.Port, cancellationToken)
			.ConfigureAwait(false);
		var packets = await config.GetStringAsync(entryId, WakeOnLanConfigKeys.PacketCount, cancellationToken)
			.ConfigureAwait(false);
		var delay = await config.GetStringAsync(entryId, WakeOnLanConfigKeys.PacketDelay, cancellationToken)
			.ConfigureAwait(false);

		if (!TryParseMac(mac, out var macBytes)
			|| !TryParseBroadcastAddress(address, out var broadcast)
			|| ParsePort(port) is not { } portNumber
			|| ParsePackets(packets) is not { } packetCount
			|| ParseDelay(delay) is not { } delayMilliseconds)
		{
			return null;
		}

		return new WakeOnLanDevice(macBytes,
			broadcast,
			portNumber,
			packetCount,
			TimeSpan.FromMilliseconds(delayMilliseconds));
	}

	public static bool TryParseMac(string? value, out byte[] mac)
	{
		mac = [];
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		var text = value.Trim();
		if (!MacRegex().IsMatch(text))
		{
			return false;
		}

		var hex = text.Replace(":", string.Empty, StringComparison.Ordinal)
			.Replace("-", string.Empty, StringComparison.Ordinal)
			.Replace(".", string.Empty, StringComparison.Ordinal);
		var bytes = Convert.FromHexString(hex);
		if (bytes.All(b => b == 0x00) || bytes.All(b => b == 0xFF))
		{
			return false;
		}

		mac = bytes;
		return true;
	}

	public static string FormatMac(byte[] mac)
	{
		return string.Join(':', mac.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
	}

	public static bool TryParseBroadcastAddress(string? value, out IPAddress address)
	{
		var text = string.IsNullOrWhiteSpace(value) ? DefaultBroadcastAddress : value.Trim();
		if (Ipv4Regex().IsMatch(text)
			&& IPAddress.TryParse(text, out var parsed)
			&& parsed.AddressFamily == AddressFamily.InterNetwork
			&& !parsed.Equals(IPAddress.Any))
		{
			address = parsed;
			return true;
		}

		address = IPAddress.None;
		return false;
	}

	public static int? ParsePort(object? value)
	{
		return ParseInRange(value, DefaultPort, MinPort, MaxPort);
	}

	public static int? ParsePackets(object? value)
	{
		return ParseInRange(value, DefaultPackets, MinPackets, MaxPackets);
	}

	public static int? ParseDelay(object? value)
	{
		return ParseInRange(value, DefaultDelayMilliseconds, MinDelayMilliseconds, MaxDelayMilliseconds);
	}

	private static int? ParseInRange(object? value, int defaultValue, int min, int max)
	{
		var number = ParseInteger(value, defaultValue);
		return number >= min && number <= max ? (int)number : null;
	}

	private static long? ParseInteger(object? value, int defaultValue)
	{
		return value switch
		{
			null => defaultValue,
			int number => number,
			long number => number,
			double number when number == Math.Floor(number) && Math.Abs(number) < int.MaxValue => (long)number,
			string text when string.IsNullOrWhiteSpace(text) => defaultValue,
			string text when long.TryParse(text.Trim(),
					NumberStyles.Integer,
					CultureInfo.InvariantCulture,
					out var parsed)
				=> parsed,
			JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt64(out var parsed) => parsed,
			JsonElement { ValueKind: JsonValueKind.String } element => ParseInteger(element.GetString(), defaultValue),
			JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => defaultValue,
			_ => null
		};
	}

	[GeneratedRegex(
		"""\A(?:[0-9a-f]{2}([:-])[0-9a-f]{2}(?:\1[0-9a-f]{2}){4}|(?:[0-9a-f]{4}\.){2}[0-9a-f]{4}|[0-9a-f]{12})\z""",
		RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
	private static partial Regex MacRegex();

	[GeneratedRegex("""\A(?:(?:0|[1-9][0-9]{0,2})\.){3}(?:0|[1-9][0-9]{0,2})\z""", RegexOptions.CultureInvariant)]
	private static partial Regex Ipv4Regex();
}
