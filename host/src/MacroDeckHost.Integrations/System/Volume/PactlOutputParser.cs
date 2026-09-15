using System.Text.RegularExpressions;

namespace MacroDeckHost.Integrations.System.Volume;

internal sealed record PactlDevice(string Name, string Description, bool? Muted, float? Volume);

internal sealed record PactlListing(IReadOnlyList<PactlDevice> Sinks, IReadOnlyList<PactlDevice> Sources);

internal readonly record struct PactlDefaults(string? Sink, string? Source);

internal static partial class PactlOutputParser
{
	private const string MonitorSuffix = ".monitor";

	// pactl localizes its labels and its yes/no, so every call runs with English messages. LC_ALL would
	// override LC_MESSAGES; its value moves to LC_CTYPE so non-ASCII device names keep their charset.
	public static IReadOnlyDictionary<string, string?> UnlocalizedEnvironment(string? lcAll)
	{
		var environment = new Dictionary<string, string?>(StringComparer.Ordinal)
		{
			["LC_ALL"] = null,
			["LC_MESSAGES"] = "C"
		};

		if (!string.IsNullOrEmpty(lcAll))
		{
			environment["LC_CTYPE"] = lcAll;
		}

		return environment;
	}

	public static PactlListing ParseListing(string text)
	{
		var sinks = new List<PactlDevice>();
		var sources = new List<PactlDevice>();
		List<PactlDevice>? section = null;
		string? name = null;
		string? description = null;
		bool? muted = null;
		float? volume = null;

		foreach (var rawLine in text.Split('\n'))
		{
			if (rawLine.Length > 0 && !char.IsWhiteSpace(rawLine[0]))
			{
				Flush();
				var header = SectionRegex().Match(rawLine.TrimEnd());
				section = !header.Success ? null : header.Groups[1].Value == "Sink" ? sinks : sources;
				continue;
			}

			var line = rawLine.Trim();
			if (section is null)
			{
				continue;
			}

			if (line.StartsWith("Name:", StringComparison.Ordinal))
			{
				name = line["Name:".Length..].Trim();
			}
			else if (line.StartsWith("Description:", StringComparison.Ordinal))
			{
				description = line["Description:".Length..].Trim();
			}
			else if (line.StartsWith("Mute:", StringComparison.Ordinal))
			{
				muted = ParseMute(line);
			}
			else if (line.StartsWith("Volume:", StringComparison.Ordinal))
			{
				volume = ParsePercent(line);
			}
		}

		Flush();
		return new PactlListing(sinks, sources);

		void Flush()
		{
			if (section is not null && name is { Length: > 0 } && !name.EndsWith(MonitorSuffix, StringComparison.Ordinal))
			{
				section.Add(new PactlDevice(name, string.IsNullOrEmpty(description) ? name : description, muted, volume));
			}

			name = null;
			description = null;
			muted = null;
			volume = null;
		}
	}

	public static PactlDefaults ParseDefaults(string text)
	{
		string? sink = null;
		string? source = null;
		foreach (var rawLine in text.Split('\n'))
		{
			var line = rawLine.Trim();
			if (line.StartsWith("Default Sink:", StringComparison.Ordinal))
			{
				sink = line["Default Sink:".Length..].Trim();
			}
			else if (line.StartsWith("Default Source:", StringComparison.Ordinal))
			{
				source = line["Default Source:".Length..].Trim();
			}
		}

		return new PactlDefaults(sink, source);
	}

	public static bool? ParseMute(string output)
	{
		var match = MuteRegex().Match(output);
		return match.Success ? match.Groups[1].Value == "yes" : null;
	}

	public static float? ParsePercent(string output)
	{
		var match = PercentRegex().Match(output);
		return match.Success && int.TryParse(match.Groups[1].Value, out var percent)
			? Math.Clamp(percent / 100f, 0f, 1f)
			: null;
	}

	[GeneratedRegex(@"\A(Sink|Source) #\d+\z")]
	private static partial Regex SectionRegex();

	[GeneratedRegex(@"Mute:\s*(yes|no)\b")]
	private static partial Regex MuteRegex();

	[GeneratedRegex(@"(\d{1,3})%")]
	private static partial Regex PercentRegex();
}
