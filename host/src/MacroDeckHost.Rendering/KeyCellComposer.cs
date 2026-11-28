using System.Text.Json;
using MacroDeckHost.Rendering.Animation;
using MacroDeckHost.Rendering.Nodes;
using MacroDeckHost.Rendering.Paint;
using MacroDeckHost.Rendering.Tree;
using SkiaSharp;

namespace MacroDeckHost.Rendering;

public enum KeyCellKind
{
	Background,

	Empty,

	Tile
}

public sealed record KeyCellInput
{
	public required int KeyWidth { get; init; }

	public required int KeyHeight { get; init; }

	public int Columns { get; init; } = 1;

	public int Rows { get; init; } = 1;

	public KeyCellKind Kind { get; init; } = KeyCellKind.Tile;

	public string? Background { get; init; }

	public double CornerRadius { get; init; }

	public double Spacing { get; init; }

	public bool EmptyCellVisible { get; init; } = true;

	public bool Borders { get; init; } = true;

	public bool AnimatedImages { get; init; } = true;

	public bool Pressed { get; init; }

	public string? WidgetType { get; init; }

	public JsonElement? WidgetData { get; init; }

	public JsonElement? Tree { get; init; }

	public RenderTheme Theme { get; init; } = RenderTheme.Dark;

	public string Culture { get; init; } = "en-US";

	public TimeZoneInfo TimeZone { get; init; } = TimeZoneInfo.Utc;

	public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;

	public IRenderLocalization? Localization { get; init; }

	public IRenderResources? Resources { get; init; }

	public IRenderFonts? Fonts { get; init; }
}

public sealed record KeyPicture(int Row, int Column, byte[] Pixels, int Width, int Height);

public sealed record KeyCellOutput
{
	public required IReadOnlyList<KeyPicture> Keys { get; init; }

	public DateTimeOffset? NextChangeAt { get; init; }

	public required IReadOnlyList<string> MissingResources { get; init; }
}

public static class KeyCellComposer
{
	private const string ActionButtonType = "ActionButton";

	private const double ReferenceCell = 120;

	private const double FilledInset = 1.5;

	private const double EmptyCellOpacity = 0.35;

	private const long RingFrameMs = 50;

	private const string DefaultBorderColor = "#ffffff";

	private static readonly HashSet<string> _ringStyles = new(StringComparer.Ordinal)
	{
		"static", "heartbeat", "breathing", "blink", "comet", "ants", "hue-shift", "rgb"
	};

	private static readonly SKSamplingOptions _sampling = new(SKFilterMode.Linear, SKMipmapMode.None);

	public static TimeSpan PressMinVisible => TimeSpan.FromMilliseconds(Motion.PressMinVisibleMs);

	public static KeyCellOutput Compose(KeyCellInput input, TileRenderer? renderer = null)
	{
		ArgumentNullException.ThrowIfNull(input);
		if (input.KeyWidth <= 0 || input.KeyHeight <= 0)
		{
			throw new ArgumentOutOfRangeException(nameof(input), "a key has a positive size");
		}

		var columns = input.Kind == KeyCellKind.Tile ? Math.Max(1, input.Columns) : 1;
		var rows = input.Kind == KeyCellKind.Tile ? Math.Max(1, input.Rows) : 1;
		var scale = Math.Min(input.KeyWidth, input.KeyHeight) / ReferenceCell;
		var gap = columns > 1 || rows > 1 ? (int)Math.Round(Math.Max(0, input.Spacing) * scale) : 0;
		var width = columns * input.KeyWidth + (columns - 1) * gap;
		var height = rows * input.KeyHeight + (rows - 1) * gap;
		var radius = (float)Math.Min(Math.Max(0, input.CornerRadius) * scale, Math.Min(width, height) / 2.0);
		var tokens = input.Theme.Tokens();

		var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
		using var surface = SKSurface.Create(info);
		var canvas = surface.Canvas;
		canvas.Clear(Token(tokens, "--color-bg-primary"));
		if (CssColor.Parse(input.Background) is { } background)
		{
			using var paint = new SKPaint { Color = background };
			canvas.DrawRect(new SKRect(0, 0, width, height), paint);
		}

		var bounds = new SKRect(0, 0, width, height);
		DateTimeOffset? next = null;
		IReadOnlyList<string> missing = [];
		switch (input.Kind)
		{
			case KeyCellKind.Empty when input.EmptyCellVisible:
				PaintEmptyCell(canvas, bounds, radius, Token(tokens, "--color-bg-secondary"));
				break;
			case KeyCellKind.Tile:
				canvas.Save();
				if (input.Pressed)
				{
					canvas.Scale((float)Motion.PressScale, (float)Motion.PressScale, width / 2f, height / 2f);
				}

				(next, missing) = PaintTile(canvas, input, bounds, radius, scale, renderer, tokens);
				canvas.Restore();
				break;
		}

		using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul));
		surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
		var pixels = bitmap.Bytes;
		var keys = new List<KeyPicture>(columns * rows);
		for (var row = 0; row < rows; row++)
		{
			for (var column = 0; column < columns; column++)
			{
				keys.Add(Slice(pixels,
					width,
					row,
					column,
					column * (input.KeyWidth + gap),
					row * (input.KeyHeight + gap),
					input.KeyWidth,
					input.KeyHeight));
			}
		}

		return new KeyCellOutput { Keys = keys, NextChangeAt = next, MissingResources = missing };
	}

	private static void PaintEmptyCell(SKCanvas canvas, SKRect bounds, float radius, SKColor color)
	{
		using var paint = new SKPaint
		{
			IsAntialias = true,
			Color = color.WithAlpha((byte)Math.Round(color.Alpha * EmptyCellOpacity))
		};
		canvas.DrawRoundRect(new SKRoundRect(bounds, radius, radius), paint);
	}

	private static (DateTimeOffset? Next, IReadOnlyList<string> Missing) PaintTile(
		SKCanvas canvas,
		KeyCellInput input,
		SKRect bounds,
		float radius,
		double scale,
		TileRenderer? renderer,
		IReadOnlyDictionary<string, string> tokens)
	{
		var root = input.Tree is { ValueKind: JsonValueKind.Object } tree && tree.TryGetProperty("root", out var value)
			? UiNode.Parse(value)
			: null;
		var box = new BoxSize(bounds.Width / scale, bounds.Height / scale);
		var face = EffectiveRoot(root, box);
		var background = UiLength.BackgroundColor(face, "background");
		var transparent = face?.Type is Components.Stack or Components.Button && background == "transparent";
		var filled = face?.Type == Components.Stack && background is not null && Opaque(background)
			|| face?.Type == Components.Button && (background is null || Opaque(background));
		var faceColor = Token(tokens, "--color-bg-secondary");
		var shape = new SKRoundRect(bounds, radius, radius);

		canvas.Save();
		canvas.ClipRoundRect(shape, SKClipOperation.Intersect, true);
		if (filled)
		{
			var inset = (float)(FilledInset * scale);
			var underlay = bounds;
			underlay.Inflate(-inset, -inset);
			using var paint = new SKPaint { IsAntialias = true, Color = faceColor };
			canvas.DrawRoundRect(new SKRoundRect(underlay, radius, radius), paint);
		}
		else if (!transparent)
		{
			using var paint = new SKPaint { IsAntialias = true, Color = faceColor };
			canvas.DrawRoundRect(shape, paint);
		}

		DateTimeOffset? next = null;
		IReadOnlyList<string> missing = [];
		if (root is not null)
		{
			var output = (renderer ?? new TileRenderer(input.Fonts)).Render(new TileRenderInput
			{
				Tree = input.Tree,
				Width = bounds.Width / scale,
				Height = bounds.Height / scale,
				Basis = Math.Min(bounds.Width, bounds.Height) / scale,
				Theme = input.Theme,
				Culture = input.Culture,
				TimeZone = input.TimeZone,
				Now = input.Now,
				Localization = input.Localization,
				Resources = input.Resources,
				Fonts = input.Fonts,
				Scale = scale,
				AnimatedImages = input.AnimatedImages,
				OwnsRootBorder = true,
				TransparentTile = true
			});
			DrawPixels(canvas, output);
			next = output.NextChangeAt;
			missing = output.MissingResources;
		}

		if (input.Pressed)
		{
			using var tint = new SKPaint
			{
				IsAntialias = true,
				Color = SKColors.White.WithAlpha((byte)Math.Round(255 * Motion.PressTintAlpha))
			};
			canvas.DrawRoundRect(shape, tint);
		}

		canvas.Restore();

		if (input.Borders && Border(input, root) is { } ring)
		{
			Rings.PaintTileRing(canvas, ring.Style, ring.Color, bounds, radius, input.Now);
			if (Rings.CycleMs.ContainsKey(ring.Style))
			{
				var now = input.Now.ToUnixTimeMilliseconds();
				var frame = DateTimeOffset.FromUnixTimeMilliseconds(now - now % RingFrameMs + RingFrameMs);
				next = next is { } earlier && earlier < frame ? earlier : frame;
			}
		}

		return (next, missing);
	}

	private static void DrawPixels(SKCanvas canvas, TileRenderOutput output)
	{
		var info = new SKImageInfo(output.PixelWidth, output.PixelHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
		using var data = SKData.CreateCopy(output.Pixels);
		using var image = SKImage.FromPixels(info, data, output.PixelWidth * 4);
		canvas.DrawImage(image, 0, 0, _sampling);
	}

	private static (string Style, SKColor Color)? Border(KeyCellInput input, UiNode? root)
	{
		string? style;
		string? color;
		if (string.Equals(input.WidgetType, ActionButtonType, StringComparison.Ordinal))
		{
			style = root?.String("borderStyle");
			color = UiLength.HexColor(root, "borderColor");
		}
		else if (input.WidgetData is { ValueKind: JsonValueKind.Object } data
			&& data.TryGetProperty("border", out var border)
			&& border.ValueKind == JsonValueKind.Object)
		{
			style = border.TryGetProperty("style", out var styleValue) && styleValue.ValueKind == JsonValueKind.String
				? styleValue.GetString()
				: null;
			color = border.TryGetProperty("color", out var colorValue) && colorValue.ValueKind == JsonValueKind.String
				? colorValue.GetString()
				: null;
		}
		else
		{
			return null;
		}

		if (style is null || !_ringStyles.Contains(style))
		{
			return null;
		}

		var resolved = string.IsNullOrEmpty(color) ? DefaultBorderColor : color;
		return (style, CssColor.Parse(resolved) ?? SKColors.White);
	}

	private static UiNode? EffectiveRoot(UiNode? root, BoxSize box)
	{
		var node = root;
		while (node is not null && node.Type == Components.Responsive)
		{
			node = ContainerComponents.ResponsiveChild(node, box);
		}

		return node;
	}

	private static bool Opaque(string background)
	{
		return background != "transparent"
			&& (background.Length != 9 || string.Equals(background[7..], "ff", StringComparison.OrdinalIgnoreCase));
	}

	private static SKColor Token(IReadOnlyDictionary<string, string> tokens, string name)
	{
		return CssColor.Parse(tokens.GetValueOrDefault(name)) ?? SKColors.Black;
	}

	private static KeyPicture Slice(
		byte[] pixels,
		int stride,
		int row,
		int column,
		int x,
		int y,
		int width,
		int height)
	{
		var key = new byte[width * height * 4];
		for (var line = 0; line < height; line++)
		{
			Buffer.BlockCopy(pixels, ((y + line) * stride + x) * 4, key, line * width * 4, width * 4);
		}

		return new KeyPicture(row, column, key, width, height);
	}
}
