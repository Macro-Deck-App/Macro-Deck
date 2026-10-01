namespace MacroDeck.Plugin.Cli.Rendering;

// Mirrors the host reference metrics (ui/runtime widget.interface.ts, GridDefaults.cs): the CLI cannot reference the host.
internal static class PreviewMetrics
{
	public const int CellSize = 120;

	public const int CellGap = 12;

	public const int DefaultRadius = 22;

	public const int MaxPixels = 8192;
}
