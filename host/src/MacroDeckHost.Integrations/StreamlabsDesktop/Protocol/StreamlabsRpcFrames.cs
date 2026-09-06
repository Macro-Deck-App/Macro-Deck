using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;

internal enum StreamlabsFrameKind
{
	Unknown,

	Result,

	Error,

	Subscription,

	Event
}

internal readonly record struct StreamlabsFrame(
	StreamlabsFrameKind Kind,
	long? Id,
	string? ResourceId,
	string? Emitter,
	JsonElement Data,
	bool IsRejected,
	int ErrorCode,
	string? ErrorMessage)
{
	public bool IsPromise => string.Equals(Emitter, StreamlabsRpcFrames.PromiseEmitter, StringComparison.Ordinal);
}

internal static class StreamlabsRpcFrames
{
	public const string StreamEmitter = "STREAM";

	public const string PromiseEmitter = "PROMISE";

	public const string AuthResource = "TcpServerService";

	public const string AuthMethod = "auth";

	public const string UnsubscribeMethod = "unsubscribe";

	public static string Request(long id, string resource, string method, IReadOnlyList<object?>? args = null)
	{
		var buffer = new ArrayBufferWriter<byte>();
		using (var writer = new Utf8JsonWriter(buffer))
		{
			writer.WriteStartObject();
			writer.WriteString("jsonrpc", "2.0");
			writer.WriteNumber("id", id);
			writer.WriteString("method", method);

			writer.WriteStartObject("params");
			writer.WriteString("resource", resource);
			writer.WriteStartArray("args");
			foreach (var argument in args ?? [])
			{
				WriteArgument(writer, argument);
			}

			writer.WriteEndArray();
			writer.WriteEndObject();

			writer.WriteEndObject();
		}

		return Encoding.UTF8.GetString(buffer.WrittenSpan);
	}

	public static string Auth(long id, string token) => Request(id, AuthResource, AuthMethod, [token]);

	public static string Subscribe(long id, string service, string observable)
		=> Request(id, service, observable);

	public static string Unsubscribe(long id, string subscriptionResourceId)
		=> Request(id, subscriptionResourceId, UnsubscribeMethod);

	public static string SceneResource(string sceneId) => $"Scene[{Quote(sceneId)}]";

	public static string SceneItemResource(string sceneId, string sceneItemId, string sourceId)
		=> $"SceneItem[{Quote(sceneId)},{Quote(sceneItemId)},{Quote(sourceId)}]";

	public static string AudioSourceResource(string sourceId) => $"AudioSource[{Quote(sourceId)}]";

	public static string SourceResource(string sourceId) => $"Source[{Quote(sourceId)}]";

	public static bool TryParse(string text, out StreamlabsFrame frame)
	{
		frame = default;

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(text);
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

			var id = root.TryGetProperty("id", out var idElement) ? ReadId(idElement) : null;

			if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
			{
				frame = new StreamlabsFrame(StreamlabsFrameKind.Error,
					id,
					ResourceId: null,
					Emitter: null,
					Data: default,
					IsRejected: true,
					ErrorCode: (int)(StreamlabsModelReader.ReadNumber(StreamlabsModelReader.Property(error, "code")) ??
						0d),
					ErrorMessage: StreamlabsModelReader.ReadString(StreamlabsModelReader.Property(error, "message")));
				return true;
			}

			if (!root.TryGetProperty("result", out var result))
			{
				return false;
			}

			var type = StreamlabsModelReader.ReadString(StreamlabsModelReader.Property(result, "_type"));
			var resourceId =
				StreamlabsModelReader.ReadString(StreamlabsModelReader.Property(result, "resourceId"));
			var emitter = StreamlabsModelReader.ReadString(StreamlabsModelReader.Property(result, "emitter"));

			switch (type)
			{
				case "SUBSCRIPTION":
					frame = new StreamlabsFrame(StreamlabsFrameKind.Subscription,
						id,
						resourceId,
						emitter,
						Data: default,
						IsRejected: false,
						ErrorCode: 0,
						ErrorMessage: null);
					return true;

				case "EVENT":
					frame = new StreamlabsFrame(StreamlabsFrameKind.Event,
						id,
						resourceId,
						emitter,
						StreamlabsModelReader.Property(result, "data").Clone(),
						StreamlabsModelReader.Property(result, "isRejected").ValueKind == JsonValueKind.True,
						ErrorCode: 0,
						ErrorMessage: null);
					return true;

				default:
					frame = new StreamlabsFrame(StreamlabsFrameKind.Result,
						id,
						resourceId,
						emitter,
						result.Clone(),
						IsRejected: false,
						ErrorCode: 0,
						ErrorMessage: null);
					return true;
			}
		}
	}

	private static void WriteArgument(Utf8JsonWriter writer, object? argument)
	{
		switch (argument)
		{
			case null:
				writer.WriteNullValue();
				break;
			case string text:
				writer.WriteStringValue(text);
				break;
			case bool flag:
				writer.WriteBooleanValue(flag);
				break;
			case int number:
				writer.WriteNumberValue(number);
				break;
			case long number:
				writer.WriteNumberValue(number);
				break;
			case double number:
				writer.WriteNumberValue(number);
				break;
			default:
				writer.WriteStringValue(Convert.ToString(argument, CultureInfo.InvariantCulture));
				break;
		}
	}

	private static long? ReadId(JsonElement value) => value.ValueKind switch
	{
		JsonValueKind.Number when value.TryGetInt64(out var number) => number,
		JsonValueKind.String when long.TryParse(value.GetString(),
			NumberStyles.Integer,
			CultureInfo.InvariantCulture,
			out var parsed) => parsed,
		_ => null
	};

	private static string Quote(string value)
		=>
			$"\"{value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}
