using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Application.Ui.Transport.Messages.MusicPlayer;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Widgets.MusicPlayer;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// The Music Player widget's <c>widget-config</c> tree (issue #837): every key the shipped schema declares
/// for it, the <c>showAlbum</c> dependency on the cover style, a stored instance that no longer resolves
/// staying visible as an unavailable selection, and the provider's decline of a foreign widget type.
/// </summary>
[TestFixture]
public class MusicPlayerWidgetConfigTests
{
	private static readonly object _stored = new
	{
		instanceId = "spotify.1",
		coverStyle = "small",
		showHeader = true,
		showTitle = true,
		showArtist = true,
		showAlbum = true,
		showTimeline = true,
		border = new { style = "static", color = "#ff0000" },
	};

	[Test]
	public void Editing_every_control_still_validates_against_the_music_player_schema()
	{
		var host = Render(_stored, "spotify.1", "spotify.2");

		host.ById("instanceId").Change("spotify.2");
		host.ById("coverStyle").Change("full");
		host.ById("showHeader").Change(false);
		host.ById("showTitle").Change(false);
		host.ById("showArtist").Change(false);
		host.ById("showTimeline").Change(false);
		host.ById("border.style").Change("comet");
		host.ById("border.color").Change("#00ff00");

		var composed = Compose(host);
		var provider = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator()));

		Assert.That(provider.TryGet(WidgetTypeIds.MusicPlayer, out var schema), Is.True);

		Assert.Multiple(() =>
		{
			Assert.That(WidgetDataSchema.Validate(schema!, composed), Is.Empty);
			Assert.That(composed.GetProperty("instanceId").GetString(), Is.EqualTo("spotify.2"));
			Assert.That(composed.GetProperty("coverStyle").GetString(), Is.EqualTo("full"));
			Assert.That(composed.GetProperty("border").GetProperty("style").GetString(), Is.EqualTo("comet"));
		});
	}

	[Test]
	public void ShowAlbum_is_visible_exactly_while_the_small_cover_style_is_selected()
	{
		var host = Render(new { coverStyle = "small" }, "spotify.1");

		Assert.That(WidgetConfigTestSupport.IsVisible(host, "showAlbum"), Is.True);

		host.ById("coverStyle").Change("full");

		Assert.That(WidgetConfigTestSupport.IsVisible(host, "showAlbum"), Is.False);
	}

	[Test]
	public void A_stored_instance_that_no_longer_resolves_stays_selected_as_an_unavailable_entry()
	{
		var host = Render(new { instanceId = "spotify.gone" }, "spotify.1");

		var node = host.ById("instanceId");

		Assert.Multiple(() =>
		{
			Assert.That(node.Text(UiConfigProperties.Value), Is.EqualTo("spotify.gone"));
			Assert.That(OptionValues(node), Does.Contain("spotify.gone"));
		});
	}

	[Test]
	public async Task A_config_surface_naming_a_different_widget_type_is_declined()
	{
		var provider = new MusicPlayerWidgetUiProvider(new StubMusicPlayerRegistry([]),
			new NullStateCache(),
			new NullArtworkService(),
			new NullPaletteExtractor(),
			new MusicPlayerStateNotifier(),
			new WidgetRenderSignals(),
			new FakeIntegrationRegistry(),
			new NullResourceStore(),
			new PassThroughSampleText(),
			TimeProvider.System,
			Serilog.Core.Logger.None);

		var surface = ConfigSurface(WidgetTypeIds.Clock, "{}");

		var session = await provider.CreateSessionAsync(new UiSessionRequest { Surface = surface, UiModelVersion = 1 },
			CancellationToken.None);

		Assert.That(session, Is.Null);
	}

	private static UiTestHost Render(object data, params string[] instanceIds)
		=> UiTestHost.Render(MusicPlayerWidgetConfigView.Build(JsonSerializer.SerializeToElement(data),
			new StubMusicPlayerRegistry(instanceIds
				.Select(id => new MusicPlayerInstanceDescriptor(id, "spotify", default, id, false))
				.ToList())));

	private static UiSurface ConfigSurface(string widgetType, string widgetData)
		=> new()
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint]
					= JsonSerializer.SerializeToElement(UiConfigEntryPoints.WidgetConfig),
				[UiConfigSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(Guid.NewGuid().ToString()),
				[UiConfigSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType),
				[UiConfigSurfaceAttributes.WidgetData] = JsonSerializer.Deserialize<JsonElement>(widgetData),
			},
		};

	private static List<string> OptionValues(UiTestNode node)
	{
		var options = node.Property(UiConfigProperties.Options);

		if (options is not { ValueKind: JsonValueKind.Array } array)
		{
			return [];
		}

		return array.EnumerateArray().Select(option => option.GetProperty("value").GetString() ?? string.Empty)
			.ToList();
	}

	private static JsonElement Compose(UiTestHost host)
	{
		var border = new Dictionary<string, object?>
		{
			["style"] = host.ById("border.style").Text(UiConfigProperties.Value),
			["color"] = host.ById("border.color").Text(UiConfigProperties.Value),
		};

		var data = new Dictionary<string, object?>
		{
			["instanceId"] = host.ById("instanceId").Text(UiConfigProperties.Value),
			["coverStyle"] = host.ById("coverStyle").Text(UiConfigProperties.Value),
			["showHeader"] = host.ById("showHeader").Flag(UiConfigProperties.Value),
			["showTitle"] = host.ById("showTitle").Flag(UiConfigProperties.Value),
			["showArtist"] = host.ById("showArtist").Flag(UiConfigProperties.Value),
			["showAlbum"] = host.ById("showAlbum").Flag(UiConfigProperties.Value),
			["showTimeline"] = host.ById("showTimeline").Flag(UiConfigProperties.Value),
			["border"] = border,
		};

		return JsonSerializer.SerializeToElement(data);
	}

	private sealed class StubMusicPlayerRegistry(IReadOnlyList<MusicPlayerInstanceDescriptor> instances)
		: IMusicPlayerRegistry
	{
		public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances() => instances;

		public IMusicPlayer? GetPlayer(string instanceId) => null;

		public IMusicPlayer? DefaultPlayer => null;
	}

	private sealed class NullStateCache : IMusicPlayerStateCache
	{
		public MusicPlayerStatePayload? GetState(string instanceId) => null;

		public IReadOnlyList<MusicPlayerStatePayload> GetAll() => [];

		public void Record(string instanceId, MusicPlayerStatePayload payload)
		{
		}

		public void Forget(IReadOnlySet<string> keep)
		{
		}
	}

	private sealed class NullArtworkService : IMusicPlayerArtworkService
	{
		public string GetETag(string artworkId, int? size) => string.Empty;

		public Task<ArtworkImageResult?> GetImage(string instanceId,
			string artworkId,
			int? size,
			CancellationToken cancellationToken)
			=> Task.FromResult<ArtworkImageResult?>(null);
	}

	private sealed class NullPaletteExtractor : IArtworkPaletteExtractor
	{
		public ArtworkPalette? Extract(byte[] image) => null;
	}

	private sealed class NullResourceStore : IUiResourceStore
	{
		public UiResource Register(UiResourceRegistration registration)
			=> new() { ResourceId = $"{registration.OwnerId}.{registration.Name}", ContentHash = "hash" };

		public bool TryGet(string resourceId, out UiResourceContent content)
		{
			content = default!;
			return false;
		}
	}

	private sealed class PassThroughSampleText : MacroDeckHost.Widgets.Preview.IWidgetSampleTextResolver
	{
		public ValueTask<string> ResolveAsync(LocalizedString value) =>
			ValueTask.FromResult(value.ToString() ?? string.Empty);
	}
}
