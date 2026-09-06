using System.Globalization;

namespace MacroDeckHost.Application.Logging;

public readonly record struct LogReadPosition(string FileStamp, long Offset)
{
	public static LogReadPosition None => new(string.Empty, 0);

	public bool HasFile => !string.IsNullOrEmpty(FileStamp);

	public int CompareTo(LogReadPosition other)
	{
		var byStamp = string.CompareOrdinal(FileStamp, other.FileStamp);

		return byStamp != 0 ? byStamp : Offset.CompareTo(other.Offset);
	}

	public string Encode()
		=> HasFile ? $"{FileStamp}:{Offset.ToString(CultureInfo.InvariantCulture)}" : string.Empty;

	public static bool TryParse(string? value, out LogReadPosition position)
	{
		position = None;
		if (string.IsNullOrEmpty(value))
		{
			return true;
		}

		var separator = value.IndexOf(':', StringComparison.Ordinal);
		if (separator <= 0 ||
			!long.TryParse(value[(separator + 1)..],
				NumberStyles.None,
				CultureInfo.InvariantCulture,
				out var offset))
		{
			return false;
		}

		position = new LogReadPosition(value[..separator], offset);

		return true;
	}
}
