using System.Globalization;

namespace MacroDeckHost.Application.Logging;

public static class LogEntryId
{
	private const int OffsetDigits = 12;

	public static string Create(LogFileKind stream, string fileStamp, long offset)
		=> $"{stream.Tag()}{fileStamp}:{offset.ToString($"D{OffsetDigits}", CultureInfo.InvariantCulture)}";

	public static bool TryParse(string? id, out LogFileKind stream, out LogReadPosition position)
	{
		stream = LogFileKind.Host;
		position = LogReadPosition.None;

		return !string.IsNullOrEmpty(id) &&
			LogFileKindExtensions.TryFromTag(id[0], out stream) &&
			LogReadPosition.TryParse(id[1..], out position);
	}
}
