using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Integrations.AutoHotkey.Protocol;

internal enum HelperMessageKind
{
	Ready,
	SetVariable,
	GetVariable,
	Notify,
	Result,
	Log,
	Error
}

internal sealed record HelperMessage(HelperMessageKind Kind)
{
	public long RequestId { get; init; }

	public string Name { get; init; } = string.Empty;

	public string Text { get; init; } = string.Empty;

	public string Title { get; init; } = string.Empty;

	public string Level { get; init; } = "info";

	public int Line { get; init; }

	public string? File { get; init; }

	public string? What { get; init; }

	public string? Extra { get; init; }
}

internal enum HelperParseStatus
{
	Output,
	Message,
	Malformed,
	Dropped,
	RejectedRequest
}

internal sealed record HelperParseResult(HelperParseStatus Status, string? Output = null)
{
	public HelperMessage? Message { get; init; }

	public long RequestId { get; init; }

	public bool OverRateLimit { get; init; }
}

internal sealed partial class HelperMessageParser
{
	public const int MaxMessageLength = 16 * 1024;
	public const int MaxMessagesPerSecond = 200;

	private readonly TimeProvider _time;
	private long _windowStart;
	private int _windowCount;

	public HelperMessageParser(TimeProvider time)
	{
		_time = time;
		_windowStart = time.GetTimestamp();
	}

	public HelperParseResult Parse(HelperLine line)
	{
		var separator = line.Text.IndexOf(HelperLineReader.RecordSeparator);
		if (separator < 0)
		{
			return new HelperParseResult(HelperParseStatus.Output, line.Text);
		}

		var output = separator > 0 ? line.Text[..separator] : null;
		var payload = line.Text[(separator + 1)..];

		if (line.Truncated)
		{
			return RequestIdOf(payload) is { } truncatedId
				? new HelperParseResult(HelperParseStatus.RejectedRequest, output) { RequestId = truncatedId }
				: new HelperParseResult(HelperParseStatus.Dropped, output);
		}

		var message = TryRead(payload);
		if (message is null)
		{
			return RequestIdOf(payload) is { } malformedId
				? new HelperParseResult(HelperParseStatus.RejectedRequest, output) { RequestId = malformedId }
				: new HelperParseResult(HelperParseStatus.Malformed, output);
		}

		// Ready, the result and an error only update the run in memory, so a flood never costs one of them.
		if (message.Kind is not (HelperMessageKind.Ready or HelperMessageKind.Result or HelperMessageKind.Error)
			&& !TryCount())
		{
			return message.Kind == HelperMessageKind.GetVariable
				? new HelperParseResult(HelperParseStatus.RejectedRequest, output)
				{
					RequestId = message.RequestId, OverRateLimit = true
				}
				: new HelperParseResult(HelperParseStatus.Dropped, output) { OverRateLimit = true };
		}

		return new HelperParseResult(HelperParseStatus.Message, output) { Message = message };
	}

	private bool TryCount()
	{
		var now = _time.GetTimestamp();
		if (_time.GetElapsedTime(_windowStart, now) >= TimeSpan.FromSeconds(1))
		{
			_windowStart = now;
			_windowCount = 0;
		}

		_windowCount++;
		return _windowCount <= MaxMessagesPerSecond;
	}

	private static long? RequestIdOf(string payload)
	{
		var match = GetRequestPrefix().Match(payload);
		return match.Success
			&& long.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
				? id
				: null;
	}

	private static HelperMessage? TryRead(string payload)
	{
		try
		{
			using var document = JsonDocument.Parse(payload);
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object || Text(root, "t") is not { } type)
			{
				return null;
			}

			return type switch
			{
				"ready" => new HelperMessage(HelperMessageKind.Ready),
				"set" when Text(root, "name") is { } name => new HelperMessage(HelperMessageKind.SetVariable)
				{
					Name = name, Text = Text(root, "value") ?? string.Empty
				},
				"get" when Number(root, "id") is { } id => new HelperMessage(HelperMessageKind.GetVariable)
				{
					RequestId = id, Name = Text(root, "name") ?? string.Empty
				},
				"notify" => new HelperMessage(HelperMessageKind.Notify)
				{
					Title = Text(root, "title") ?? string.Empty,
					Text = Text(root, "message") ?? string.Empty,
					Level = Text(root, "level") ?? "info"
				},
				"result" => new HelperMessage(HelperMessageKind.Result) { Text = Text(root, "text") ?? string.Empty },
				"log" => new HelperMessage(HelperMessageKind.Log) { Text = Text(root, "text") ?? string.Empty },
				"error" => new HelperMessage(HelperMessageKind.Error)
				{
					Text = Text(root, "message") ?? string.Empty,
					Line = (int)Math.Clamp(Number(root, "line") ?? 0, 0, int.MaxValue),
					File = Text(root, "file"),
					What = Text(root, "what"),
					Extra = Text(root, "extra")
				},
				_ => null
			};
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static string? Text(JsonElement root, string name)
	{
		return root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;
	}

	private static long? Number(JsonElement root, string name)
	{
		return root.TryGetProperty(name, out var value)
			&& value.ValueKind == JsonValueKind.Number
			&& value.TryGetInt64(out var number)
				? number
				: null;
	}

	[GeneratedRegex("""\A\{"t":"get","id":(\d{1,18})[,}]""", RegexOptions.CultureInvariant)]
	private static partial Regex GetRequestPrefix();
}
