using System.Text.Json;

namespace MacroDeckHost.Rendering;

public interface IRenderLocalization
{
	string Translate(string scope, string key, IReadOnlyDictionary<string, string>? arguments);
}

public sealed record RenderResource(
	ReadOnlyMemory<byte> Data,
	string? MediaType,
	string? ContentHash = null,
	object? Owner = null);

public interface IRenderResources
{
	RenderResource? TryGet(string resourceId, int? displayPx);
}

public sealed record TileRenderInput
{
	public JsonElement? Tree { get; init; }

	public double Width { get; init; } = 120;

	public double Height { get; init; } = 120;

	public double Basis { get; init; } = 120;

	public RenderTheme Theme { get; init; } = RenderTheme.Dark;

	public string Culture { get; init; } = "en-US";

	public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

	public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;

	public IRenderLocalization? Localization { get; init; }

	public IRenderResources? Resources { get; init; }

	public IRenderFonts? Fonts { get; init; }

	public double Scale { get; init; } = 1;

	public bool AnimatedImages { get; init; } = true;

	public bool OwnsRootBorder { get; init; }

	// A deck key paints the tile face itself, so the tree is drawn onto a transparent tile.
	internal bool TransparentTile { get; init; }
}
