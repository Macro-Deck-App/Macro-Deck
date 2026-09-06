namespace MacroDeckHost.Application.Logging;

public readonly record struct LogCursor(LogReadPosition Host, LogReadPosition Bootstrapper)
{
	private const string Version = "1";

	public static LogCursor None => new(LogReadPosition.None, LogReadPosition.None);

	public LogReadPosition For(LogFileKind stream)
		=> stream == LogFileKind.Bootstrapper ? Bootstrapper : Host;

	public LogCursor With(LogFileKind stream, LogReadPosition position)
		=> stream == LogFileKind.Bootstrapper
			? this with { Bootstrapper = position }
			: this with { Host = position };

	public bool IsExhausted => !Host.HasFile && !Bootstrapper.HasFile;

	public string Encode() => $"{Version}|h{Host.Encode()}|b{Bootstrapper.Encode()}";

	public static bool TryParse(string? value, out LogCursor cursor)
	{
		cursor = None;
		if (string.IsNullOrWhiteSpace(value))
		{
			return false;
		}

		var parts = value.Split('|');
		if (parts.Length != 3 || parts[0] != Version)
		{
			return false;
		}

		var host = LogReadPosition.None;
		var bootstrapper = LogReadPosition.None;

		foreach (var part in parts.AsSpan(1))
		{
			if (part.Length == 0 ||
				!LogFileKindExtensions.TryFromTag(part[0], out var stream) ||
				!LogReadPosition.TryParse(part[1..], out var position))
			{
				return false;
			}

			if (stream == LogFileKind.Bootstrapper)
			{
				bootstrapper = position;
			}
			else
			{
				host = position;
			}
		}

		cursor = new LogCursor(host, bootstrapper);

		return true;
	}
}
