using System.Text.RegularExpressions;
using MacroDeck.Localization;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Devices.Surfaces.KeyImages;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Rendering;

namespace MacroDeckHost.Infrastructure.Rendering;

public sealed partial class SkiaDeviceKeyImageRenderer : IDeviceKeyImageRenderer, IDisposable
{
	private const string LightTheme = "light";

	private const int RegularWeight = 400;

	private const int NormalWidth = 5;

	private readonly IFontCatalog _fontCatalog;
	private readonly ILocalizationResolver _localization;
	private readonly IUserFontLibrary? _userFonts;
	private readonly Lock _gate = new();
	private readonly Dictionary<string, FontsEntry> _fonts = new(StringComparer.OrdinalIgnoreCase);

	public SkiaDeviceKeyImageRenderer(
		IFontCatalog fontCatalog,
		ILocalizationResolver localization,
		IUserFontLibrary? userFonts = null)
	{
		_fontCatalog = fontCatalog;
		_localization = localization;
		_userFonts = userFonts;
		if (_userFonts is not null)
		{
			_userFonts.Changed += OnUserFontsChanged;
		}
	}

	public event EventHandler? FontsChanged;

	public TimeSpan PressMinVisible => KeyCellComposer.PressMinVisible;

	public DeviceKeyImageRenderResult Render(DeviceKeyImageRenderRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		var rotation = request.Format.Rotation;
		var turned = rotation is 90 or 270;
		var entry = Acquire(request.Appearance.FontFamily);
		try
		{
			return Render(request, rotation, turned, entry.Fonts);
		}
		finally
		{
			Release(entry);
		}
	}

	public void Dispose()
	{
		if (_userFonts is not null)
		{
			_userFonts.Changed -= OnUserFontsChanged;
		}

		Retire();
	}

	private DeviceKeyImageRenderResult Render(
		DeviceKeyImageRenderRequest request,
		int rotation,
		bool turned,
		RenderFonts fonts)
	{
		var resources = new Resources(request.Resources);
		var output = KeyCellComposer.Compose(new KeyCellInput
			{
				KeyWidth = turned ? request.KeySize.Height : request.KeySize.Width,
				KeyHeight = turned ? request.KeySize.Width : request.KeySize.Height,
				Columns = request.Columns,
				Rows = request.Rows,
				Kind = request.Kind switch
				{
					DeviceKeyCellKind.Widget => KeyCellKind.Tile,
					DeviceKeyCellKind.Empty => KeyCellKind.Empty,
					_ => KeyCellKind.Background
				},
				Background = request.BackgroundColor,
				CornerRadius = request.CornerRadius,
				Spacing = request.Spacing,
				EmptyCellVisible = request.EmptyCellVisible,
				Borders = request.Borders,
				AnimatedImages = request.AnimatedIcons,
				Pressed = request.Pressed,
				WidgetType = request.WidgetType,
				WidgetData = request.WidgetData,
				Tree = request.Tree,
				Theme = ThemeOf(request.Appearance),
				Culture = request.Culture,
				TimeZone = request.TimeZone,
				Now = request.Now,
				Localization = new Localization(_localization, request.Culture),
				Resources = resources,
				Fonts = fonts
			},
			new TileRenderer(fonts));

		var format = FormatOf(request.Format.Encoding);
		var keys = output.Keys
			.Select(key =>
			{
				var encoded = KeyImageEncoder.Encode(key.Pixels,
					key.Width,
					key.Height,
					format,
					request.Format.MirrorHorizontal,
					rotation);
				return new DeviceKeyImagePicture(key.Row, key.Column, encoded.Data, encoded.ContentType);
			})
			.ToList();
		return new DeviceKeyImageRenderResult(keys, output.NextChangeAt, resources.Missing);
	}

	private void OnUserFontsChanged(object? sender, EventArgs e)
	{
		Retire();
		FontsChanged?.Invoke(this, EventArgs.Empty);
	}

	private void Retire()
	{
		var idle = new List<FontsEntry>();
		lock (_gate)
		{
			foreach (var entry in _fonts.Values)
			{
				entry.Retired = true;
				if (entry.Users == 0)
				{
					idle.Add(entry);
				}
			}

			_fonts.Clear();
		}

		foreach (var entry in idle)
		{
			entry.UiFace?.Dispose();
		}
	}

	// A device has no colour scheme of its own, so following the system means the dark tokens.
	private static RenderTheme ThemeOf(DeviceKeyImageAppearance appearance)
	{
		var theme = string.Equals(appearance.ThemeMode, LightTheme, StringComparison.OrdinalIgnoreCase)
			? RenderTheme.Light
			: RenderTheme.Dark;
		return appearance.AccentColor is { } accent && HexColor().IsMatch(accent) ? theme.WithAccent(accent) : theme;
	}

	private static KeyImageFormat FormatOf(string encoding)
	{
		return encoding switch
		{
			KeyImageEncodings.Rgba => KeyImageFormat.Rgba,
			KeyImageEncodings.Png => KeyImageFormat.Png,
			KeyImageEncodings.Jpeg => KeyImageFormat.Jpeg,
			KeyImageEncodings.Bmp => KeyImageFormat.Bmp,
			_ => throw new ArgumentOutOfRangeException(nameof(encoding), encoding, "unknown key image encoding")
		};
	}

	private FontsEntry Acquire(string? family)
	{
		var key = family?.Trim() ?? string.Empty;
		lock (_gate)
		{
			if (!_fonts.TryGetValue(key, out var entry))
			{
				var uiFace = key.Length == 0 ? null : UiFace(key);
				entry = new FontsEntry(new RenderFonts(uiFace), uiFace);
				_fonts[key] = entry;
			}

			entry.Users++;
			return entry;
		}
	}

	private void Release(FontsEntry entry)
	{
		lock (_gate)
		{
			entry.Users--;
			if (!entry.Retired || entry.Users > 0)
			{
				return;
			}
		}

		entry.UiFace?.Dispose();
	}

	private RenderFontFace? UiFace(string family)
	{
		var face = _fontCatalog.GetFaces()
			.Where(candidate => string.Equals(candidate.Family, family, StringComparison.OrdinalIgnoreCase))
			.OrderBy(candidate => Upright(candidate) ? 0 : 1)
			.ThenBy(candidate => Math.Abs(candidate.Weight - RegularWeight))
			.ThenBy(candidate => Math.Abs(candidate.Width - NormalWidth))
			.FirstOrDefault();
		if (face is null || _fontCatalog.GetFaceFile(face.FaceId) is not { } data)
		{
			return null;
		}

		try
		{
			return new RenderFontFace(face.Family, data);
		}
		catch (ArgumentException)
		{
			return null;
		}
	}

	private static bool Upright(FontFaceInfo face)
	{
		return string.Equals(face.Slant, FontFaceIdentity.UprightSlant, StringComparison.Ordinal);
	}

	[GeneratedRegex("^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")]
	private static partial Regex HexColor();

	private sealed class FontsEntry
	{
		public FontsEntry(RenderFonts fonts, RenderFontFace? uiFace)
		{
			Fonts = fonts;
			UiFace = uiFace;
		}

		public RenderFonts Fonts { get; }

		public RenderFontFace? UiFace { get; }

		public int Users { get; set; }

		public bool Retired { get; set; }
	}

	private sealed class Localization : IRenderLocalization
	{
		private readonly ILocalizationResolver _resolver;
		private readonly string _culture;

		public Localization(ILocalizationResolver resolver, string culture)
		{
			_resolver = resolver;
			_culture = culture;
		}

		public string Translate(string scope, string key, IReadOnlyDictionary<string, string>? arguments)
		{
			if (string.IsNullOrWhiteSpace(scope) || string.IsNullOrWhiteSpace(key))
			{
				return $"[[{scope}:{key}]]";
			}

			var values = arguments?.ToDictionary(pair => pair.Key,
				object? (pair) => pair.Value,
				StringComparer.Ordinal);
			return _resolver.Resolve(new LocalizedString(new LocalizationKey(scope, key), values), _culture);
		}
	}

	private sealed class Resources : IRenderResources
	{
		private readonly IDeviceKeyImageResourceLookup? _lookup;

		public Resources(IDeviceKeyImageResourceLookup? lookup)
		{
			_lookup = lookup;
		}

		public List<DeviceKeyImageResourceRequest> Missing { get; } = [];

		public RenderResource? TryGet(string resourceId, int? displayPx)
		{
			if (_lookup?.TryGet(resourceId, displayPx) is { } found)
			{
				return new RenderResource(found.Content, found.MediaType, found.ContentHash, found);
			}

			Missing.Add(new DeviceKeyImageResourceRequest(resourceId, displayPx));
			return null;
		}
	}
}
