namespace MacroDeck.LicenseTool.Model;

internal enum Ecosystem
{
	NuGet,
	Npm,
	Cargo,
	Asset,
}

internal enum LicenseTextKind
{
	License,
	Notice,
}

internal sealed record LicenseText(string Title, string Content, LicenseTextKind Kind, bool IsAdditional = false);

internal sealed class Component(Ecosystem ecosystem, string name)
{
	public Ecosystem Ecosystem { get; } = ecosystem;

	public string Name { get; } = name;

	public SortedSet<string> Versions { get; } = new(StringComparer.Ordinal);

	public SortedSet<string> DeclaredLicenses { get; } = new(StringComparer.Ordinal);

	public SortedSet<string> Platforms { get; } = new(StringComparer.Ordinal);

	public List<LicenseText> Texts { get; } = [];

	public string? Url { get; set; }

	public string? Copyright { get; set; }

	public string? Note { get; set; }

	public SortedSet<string> SelectedLicenses { get; } = new(StringComparer.Ordinal);

	public string Key => KeyOf(Ecosystem, Name);

	public static string KeyOf(Ecosystem ecosystem, string name) =>
		$"{ecosystem.ToString().ToUpperInvariant()}:{name.ToUpperInvariant()}";

	public void AddText(LicenseText text)
	{
		var normalized = TextNormalizer.Normalize(text.Content);
		if (normalized.Length == 0 || Texts.Any(existing => existing.Content == normalized))
		{
			return;
		}

		Texts.Add(text with { Content = normalized });
	}

	public void MergeFrom(Component other)
	{
		Versions.UnionWith(other.Versions);
		DeclaredLicenses.UnionWith(other.DeclaredLicenses);
		Platforms.UnionWith(other.Platforms);
		foreach (var text in other.Texts)
		{
			AddText(text);
		}

		Url ??= other.Url;
		Copyright ??= other.Copyright;
	}
}
