using MacroDeckHost.Application.Adb;

namespace MacroDeckHost.Infrastructure.Adb;

internal static class AdbDeviceListParser
{
	private const string HeaderLine = "List of devices attached";
	private const string NoPermissionsToken = "no permissions";

	public static IReadOnlyList<AdbDevice> Parse(string output, DateTimeOffset now)
	{
		var devices = new List<AdbDevice>();

		foreach (var rawLine in output.Split('\n'))
		{
			var line = rawLine.Trim('\r').Trim();
			if (line.Length == 0 || line == HeaderLine)
			{
				continue;
			}

			var device = TryParseLine(line, now);
			if (device is not null)
			{
				devices.Add(device);
			}
		}

		return devices;
	}

	private static AdbDevice? TryParseLine(string line, DateTimeOffset now)
	{
		try
		{
			var serialEnd = IndexOfWhitespace(line);
			if (serialEnd <= 0)
			{
				return null;
			}

			var serial = line[..serialEnd];
			var remainder = line[serialEnd..].TrimStart();
			if (remainder.Length == 0)
			{
				return null;
			}

			if (remainder.StartsWith(NoPermissionsToken, StringComparison.Ordinal))
			{
				return new AdbDevice(serial, AdbDeviceState.NoPermissions, null, null, null, null, null, now);
			}

			var stateEnd = IndexOfWhitespace(remainder);
			var stateToken = stateEnd < 0 ? remainder : remainder[..stateEnd];
			var metadata = stateEnd < 0 ? string.Empty : remainder[stateEnd..];

			var state = ParseState(stateToken);
			var (model, product, transportId) = ParseMetadata(metadata);

			return new AdbDevice(serial, state, model, null, product, transportId, null, now);
		}
		catch
		{
			return null;
		}
	}

	private static AdbDeviceState ParseState(string token)
		=> token switch
		{
			"device" => AdbDeviceState.Device,
			"unauthorized" => AdbDeviceState.Unauthorized,
			"offline" => AdbDeviceState.Offline,
			"authorizing" => AdbDeviceState.Authorizing,
			"bootloader" => AdbDeviceState.Bootloader,
			"recovery" => AdbDeviceState.Recovery,
			"sideload" => AdbDeviceState.Sideload,
			_ => AdbDeviceState.Unknown
		};

	private static (string? Model, string? Product, string? TransportId) ParseMetadata(string metadata)
	{
		string? model = null;
		string? product = null;
		string? transportId = null;

		foreach (var token in metadata.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
		{
			var separator = token.IndexOf(':');
			if (separator <= 0)
			{
				continue;
			}

			var key = token[..separator];
			var value = token[(separator + 1)..];

			switch (key)
			{
				case "model":
					model = value;
					break;
				case "product":
					product = value;
					break;
				case "transport_id":
					transportId = value;
					break;
			}
		}

		return (model, product, transportId);
	}

	private static int IndexOfWhitespace(string value)
	{
		for (var i = 0; i < value.Length; i++)
		{
			if (char.IsWhiteSpace(value[i]))
			{
				return i;
			}
		}

		return -1;
	}
}
