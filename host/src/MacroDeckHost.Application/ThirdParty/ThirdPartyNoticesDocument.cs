namespace MacroDeckHost.Application.ThirdParty;

public sealed record ThirdPartyComponent(
	string Name,
	string Ecosystem,
	IReadOnlyList<string> Licenses,
	IReadOnlyList<string> Declared,
	string? Url,
	IReadOnlyList<string> Platforms,
	string? Note,
	IReadOnlyList<int> TextIds);

public sealed record ThirdPartyText(int Id, string Content);

public sealed record ThirdPartyNoticesDocument(
	IReadOnlyList<ThirdPartyComponent> Components,
	IReadOnlyList<ThirdPartyText> Texts)
{
	private const int RuleWidth = 80;
	private const string TextsHeading = "License and notice texts";

	private static readonly (string Prefix, string Ecosystem)[] Sections =
	[
		("NuGet packages", "nuget"),
		("npm packages", "npm"),
		("Rust crates", "cargo"),
		("Bundled assets", "asset"),
	];

	public static ThirdPartyNoticesDocument Parse(string text)
	{
		var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
		var sectionRule = new string('=', RuleWidth);
		var textRule = new string('-', RuleWidth);
		var components = new List<ThirdPartyComponent>();
		string? ecosystem = null;
		var index = 0;

		while (index < lines.Length)
		{
			if (lines[index] == sectionRule && index + 2 < lines.Length && lines[index + 2] == sectionRule)
			{
				var heading = lines[index + 1];
				index += 3;
				if (heading == TextsHeading)
				{
					break;
				}

				ecosystem = Sections.FirstOrDefault(section => heading.StartsWith(section.Prefix, StringComparison.Ordinal)).Ecosystem
					?? throw new FormatException($"unknown section '{heading}'");
				continue;
			}

			if (ecosystem is not null && lines[index].Length > 0 && !lines[index].StartsWith(' '))
			{
				index = ReadComponent(lines, index, ecosystem, components);
				continue;
			}

			index++;
		}

		var texts = ReadTexts(lines, index, textRule, components.SelectMany(component => component.TextIds).DefaultIfEmpty().Max());
		return new ThirdPartyNoticesDocument(components, texts);
	}

	private static int ReadComponent(string[] lines, int index, string ecosystem, List<ThirdPartyComponent> components)
	{
		var name = lines[index++];
		var fields = new Dictionary<string, string>(StringComparer.Ordinal);
		while (index < lines.Length && lines[index].StartsWith("  ", StringComparison.Ordinal))
		{
			var separator = lines[index].IndexOf(": ", StringComparison.Ordinal);
			if (separator > 2)
			{
				fields[lines[index][2..separator]] = lines[index][(separator + 2)..];
			}

			index++;
		}

		components.Add(new ThirdPartyComponent(
			name,
			ecosystem,
			Split(fields.GetValueOrDefault("License"), "; "),
			Split(fields.GetValueOrDefault("Declared"), "; "),
			fields.GetValueOrDefault("URL"),
			Split(fields.GetValueOrDefault("Platforms"), ", "),
			fields.GetValueOrDefault("Note"),
			Split(fields.GetValueOrDefault("Texts"), ", ")
				.Select(reference => int.Parse(reference.Trim('[', ']'), System.Globalization.CultureInfo.InvariantCulture))
				.ToList()));
		return index;
	}

	private static List<ThirdPartyText> ReadTexts(string[] lines, int index, string textRule, int count)
	{
		var texts = new List<ThirdPartyText>();
		for (var id = 1; id <= count; id++)
		{
			index = FindTextHeader(lines, index, id, textRule) + 2;
			var end = id < count ? FindTextHeader(lines, index, id + 1, textRule) : lines.Length;
			var body = lines[index..end];
			texts.Add(new ThirdPartyText(id, string.Join('\n', body).TrimEnd('\n')));
			index = end;
		}

		return texts;
	}

	private static int FindTextHeader(string[] lines, int start, int id, string textRule)
	{
		var prefix = $"[{id}] ";
		for (var index = start; index + 1 < lines.Length; index++)
		{
			if (lines[index].StartsWith(prefix, StringComparison.Ordinal) && lines[index + 1] == textRule
				&& (index == 0 || lines[index - 1].Length == 0))
			{
				return index;
			}
		}

		throw new FormatException($"license text [{id}] is missing");
	}

	private static List<string> Split(string? value, string separator) =>
		string.IsNullOrEmpty(value) ? [] : [.. value.Split(separator, StringSplitOptions.RemoveEmptyEntries)];
}
