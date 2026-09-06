using System.Text.Json;

namespace MacroDeckHost.Integrations.YtmDesktop.Protocol;

internal enum EngineIoType
{
	Open,
	Close,
	Ping,
	Pong,
	Message,
	Unknown
}

internal enum SocketIoType
{
	Connect,
	Disconnect,
	Event,
	Ack,
	ConnectError,
	Unknown
}

internal readonly record struct SocketIoFrame(
	EngineIoType Engine,
	SocketIoType Socket,
	string? Namespace,
	string Payload);

internal static class SocketIoFrames
{
	public const string Namespace = "/api/v1/realtime";

	public static bool TryParse(string frame, out SocketIoFrame parsed)
	{
		parsed = default;

		if (string.IsNullOrEmpty(frame))
		{
			return false;
		}

		var engine = ParseEngineType(frame[0]);
		if (engine == EngineIoType.Unknown)
		{
			return false;
		}

		var rest = frame[1..];
		if (engine != EngineIoType.Message)
		{
			parsed = new SocketIoFrame(engine, SocketIoType.Unknown, null, rest);
			return true;
		}

		if (rest.Length == 0)
		{
			return false;
		}

		var socket = ParseSocketType(rest[0]);
		if (socket == SocketIoType.Unknown)
		{
			return false;
		}

		var index = 1;
		string? ns = null;
		if (index < rest.Length && rest[index] == '/')
		{
			var comma = rest.IndexOf(',', index);
			if (comma < 0)
			{
				return false;
			}

			ns = rest[index..comma];
			index = comma + 1;
		}

		while (index < rest.Length && char.IsAsciiDigit(rest[index]))
		{
			index++;
		}

		parsed = new SocketIoFrame(engine, socket, ns, rest[index..]);
		return true;
	}

	public static bool TryReadOpen(string payload, out string sid, out TimeSpan pingInterval, out TimeSpan pingTimeout)
	{
		sid = string.Empty;
		pingInterval = TimeSpan.FromSeconds(25);
		pingTimeout = TimeSpan.FromSeconds(20);

		if (string.IsNullOrEmpty(payload))
		{
			return false;
		}

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(payload);
		}
		catch (JsonException)
		{
			return false;
		}

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return false;
			}

			if (root.TryGetProperty("sid", out var sidElement) && sidElement.ValueKind == JsonValueKind.String)
			{
				sid = sidElement.GetString() ?? string.Empty;
			}

			if (TryGetMilliseconds(root, "pingInterval", out var intervalMs))
			{
				pingInterval = TimeSpan.FromMilliseconds(intervalMs);
			}

			if (TryGetMilliseconds(root, "pingTimeout", out var timeoutMs))
			{
				pingTimeout = TimeSpan.FromMilliseconds(timeoutMs);
			}

			return true;
		}
	}

	public static bool TryReadEvent(string payload, out string name, out JsonElement argument)
	{
		name = string.Empty;
		argument = default;

		if (string.IsNullOrEmpty(payload))
		{
			return false;
		}

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(payload);
		}
		catch (JsonException)
		{
			return false;
		}

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() != 2)
			{
				return false;
			}

			var nameElement = root[0];
			if (nameElement.ValueKind != JsonValueKind.String)
			{
				return false;
			}

			name = nameElement.GetString() ?? string.Empty;
			argument = root[1].Clone();
			return true;
		}
	}

	public static string Connect(string token) => $"40{Namespace},{{\"token\":{JsonSerializer.Serialize(token)}}}";

	public static string Disconnect() => $"41{Namespace},";

	public static string Pong() => "3";

	public static bool IsRealtimeNamespace(string? ns) => string.Equals(ns, Namespace, StringComparison.Ordinal);

	private static bool TryGetMilliseconds(JsonElement root, string property, out int milliseconds)
	{
		milliseconds = 0;
		return root.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetInt32(out milliseconds);
	}

	private static EngineIoType ParseEngineType(char c)
		=> c switch
		{
			'0' => EngineIoType.Open,
			'1' => EngineIoType.Close,
			'2' => EngineIoType.Ping,
			'3' => EngineIoType.Pong,
			'4' => EngineIoType.Message,
			_ => EngineIoType.Unknown
		};

	private static SocketIoType ParseSocketType(char c)
		=> c switch
		{
			'0' => SocketIoType.Connect,
			'1' => SocketIoType.Disconnect,
			'2' => SocketIoType.Event,
			'3' => SocketIoType.Ack,
			'4' => SocketIoType.ConnectError,
			_ => SocketIoType.Unknown
		};
}
