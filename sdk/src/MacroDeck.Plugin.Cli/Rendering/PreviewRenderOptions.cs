namespace MacroDeck.Plugin.Cli.Rendering;

internal enum PreviewTheme
{
	Dark,
	Light
}

internal sealed record PreviewRenderOptions
{
	public string? Project { get; init; }

	public string? Executable { get; init; }

	public string? Artifact { get; init; }

	public IReadOnlyList<string> Sizes { get; init; } = [];

	public IReadOnlyList<string> Cells { get; init; } = [];

	public IReadOnlyList<string> Previews { get; init; } = [];

	public double Scale { get; init; } = 2;

	public PreviewTheme Theme { get; init; } = PreviewTheme.Dark;

	public string Background { get; init; } = "transparent";

	public int Radius { get; init; } = PreviewMetrics.DefaultRadius;

	public string Locale { get; init; } = "en-US";

	public string? VideoStreamImage { get; init; }

	public string Output { get; init; } = "previews";

	public string? Browser { get; init; }
}
