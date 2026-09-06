using System.Reflection;
using System.Runtime.CompilerServices;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Ui.Resources;

namespace MacroDeckHost.Widgets.MusicPlayer;

/// <summary>
/// The Music Player's own marks, registered with the UI resource store once per store and handed to the
/// view as resolved handles - the same arrangement <c>WeatherWidgetIcons</c> uses, and for the same
/// reason: a widget tree names bytes the host serves, so a mark is a resource rather than something a
/// renderer has to know how to draw.
///
/// <para>
/// <b>Two of them animate, and the animation lives inside the SVG</b> rather than in the profile: the
/// spinning disc and the rising equaliser bars are one document each, drawn by whatever the reader shows
/// an image with. That is what keeps them renderer-agnostic - a Compose client that can show an animated
/// SVG gets them for free, and one that cannot still draws the right mark, standing still.
/// </para>
/// </summary>
internal static class MusicPlayerWidgetIcons
{
	internal const string OwnerId = "app.macro-deck.music-player";

	private static readonly string[] _iconNames =
		["music-note", "disc", "disc-spinning", "paused", "playing", "warning"];

	private static readonly ConditionalWeakTable<IUiResourceStore, Lazy<MusicPlayerIconResources>> _registrations =
		new();

	internal static MusicPlayerIconResources EnsureRegistered(IUiResourceStore store)
	{
		ArgumentNullException.ThrowIfNull(store);

		var lazy = _registrations.GetValue(store,
			static registryStore => new Lazy<MusicPlayerIconResources>(() => RegisterAll(registryStore)));

		return lazy.Value;
	}

	private static MusicPlayerIconResources RegisterAll(IUiResourceStore store)
	{
		var assembly = typeof(MusicPlayerWidgetIcons).Assembly;
		var icons = new Dictionary<string, UiResource>(_iconNames.Length, StringComparer.Ordinal);

		foreach (var name in _iconNames)
		{
			icons[name] = store.Register(new UiResourceRegistration
			{
				OwnerId = OwnerId,
				Name = name,
				MediaType = "image/svg+xml",
				Content = ReadEmbeddedSvg(assembly, $".Icons.{name}.svg"),
			});
		}

		return new MusicPlayerIconResources(icons["music-note"],
			icons["disc"],
			icons["disc-spinning"],
			icons["paused"],
			icons["playing"],
			icons["warning"]);
	}

	private static byte[] ReadEmbeddedSvg(Assembly assembly, string suffix)
	{
		var resourceName = Array.Find(assembly.GetManifestResourceNames(),
			candidate => candidate.EndsWith(suffix, StringComparison.Ordinal));

		if (resourceName is null)
		{
			throw new InvalidOperationException($"No embedded resource ending with '{suffix}' was found in " +
				$"'{assembly.GetName().Name}'.");
		}

		using var stream = assembly.GetManifestResourceStream(resourceName)!;
		using var memory = new MemoryStream();
		stream.CopyTo(memory);

		return memory.ToArray();
	}
}

/// <param name="MusicNote">Shown where no track is loaded at all.</param>
/// <param name="Disc">Shown where a track is loaded but is not playing.</param>
/// <param name="DiscSpinning">The same disc, turning - shown while a track is playing.</param>
/// <param name="Paused">The badge that says playback is paused.</param>
/// <param name="Playing">The badge that says playback is running.</param>
/// <param name="Warning">The badge that says the widget cannot show live state.</param>
internal sealed record MusicPlayerIconResources(
	UiResource MusicNote,
	UiResource Disc,
	UiResource DiscSpinning,
	UiResource Paused,
	UiResource Playing,
	UiResource Warning);
