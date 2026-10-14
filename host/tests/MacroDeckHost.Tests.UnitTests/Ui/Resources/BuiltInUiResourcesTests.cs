using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Tests.UnitTests.Ui.Resources.BundledIcons;

namespace MacroDeckHost.Tests.UnitTests.Ui.Resources;

[TestFixture]
public class BuiltInUiResourcesTests
{
	private const string IntegrationId = "app.macro-deck.example";

	private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 1, 2, 3];

	private UiResourceStore _store = null!;

	[SetUp]
	public void SetUp()
	{
		_store = new UiResourceStore();
	}

	private BuiltInUiResources Create(IMusicPlayerArtworkService? artwork = null)
	{
		return new BuiltInUiResources(IntegrationId, _store, typeof(BundledIconsIntegration), artwork);
	}

	[Test]
	public async Task A_registered_image_is_served_under_the_integrations_own_id()
	{
		var resources = Create();

		var handle = await resources.RegisterAsync("cover", _png, "image/png");

		Assert.Multiple(() =>
		{
			Assert.That(handle.ResourceId, Is.EqualTo(IntegrationId + ".cover"));
			Assert.That(_store.TryGet(handle.ResourceId, out var served), Is.True);
			Assert.That(served.Content.ToArray(), Is.EqualTo(_png));
		});
	}

	[Test]
	public async Task Registering_a_name_again_replaces_its_bytes_under_the_same_id()
	{
		var resources = Create();

		var first = await resources.RegisterAsync("cover", _png, "image/png");
		var second = await resources.RegisterAsync("cover", new byte[] { 0x89, 0x50, 9 }, "image/png");

		Assert.Multiple(() =>
		{
			Assert.That(second.ResourceId, Is.EqualTo(first.ResourceId));
			Assert.That(second.ContentHash, Is.Not.EqualTo(first.ContentHash));
			Assert.That(_store.TryGet(second.ResourceId, out var served) && served.Content.Length == 3, Is.True);
		});
	}

	[Test]
	public async Task A_removed_image_is_no_longer_served()
	{
		var resources = Create();
		var handle = await resources.RegisterAsync("cover", _png, "image/png");

		await resources.RemoveAsync("cover");

		Assert.That(_store.TryGet(handle.ResourceId, out _), Is.False);
	}

	[Test]
	public void The_plugin_rules_for_names_and_media_types_apply()
	{
		var resources = Create();

		Assert.Multiple(() =>
		{
			Assert.ThrowsAsync<ArgumentException>(() => resources.RegisterAsync("not a name", _png, "image/png"));
			Assert.ThrowsAsync<ArgumentException>(() =>
				resources.RegisterAsync("logo", "<svg/>"u8.ToArray(), "image/svg+xml"));
			Assert.ThrowsAsync<ArgumentException>(()
				=> resources.RegisterAsync("empty", Array.Empty<byte>(), "image/png"));
		});
	}

	[Test]
	public async Task Registering_beyond_the_plugin_quota_is_refused_and_keeps_what_the_name_held()
	{
		var resources = Create();
		for (var index = 0; index < ProtocolLimits.MaxUiResourcesPerPlugin; index++)
		{
			await resources.RegisterAsync("r" + index, _png, "image/png");
		}

		var exception = Assert.ThrowsAsync<UiResourceException>(() =>
			resources.RegisterAsync("one-too-many", _png, "image/png"));
		var replaced = await resources.RegisterAsync("r0", new byte[] { 1, 2 }, "image/png");

		Assert.Multiple(() =>
		{
			Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.QuotaExceeded));
			Assert.That(_store.TryGet(IntegrationId + ".one-too-many", out _), Is.False);
			Assert.That(_store.TryGet(replaced.ResourceId, out var served) && served.Content.Length == 2, Is.True);
		});
	}

	[Test]
	public async Task A_bundled_svg_icon_is_served_without_counting_against_the_quota()
	{
		var resources = Create();

		var icon = await resources.GetPluginIconAsync("widget", "shield");
		for (var index = 0; index < ProtocolLimits.MaxUiResourcesPerPlugin; index++)
		{
			await resources.RegisterAsync("r" + index, _png, "image/png");
		}

		Assert.Multiple(() =>
		{
			Assert.That(icon.ResourceId, Is.EqualTo(IntegrationId + ".icon.widget.shield"));
			Assert.That(icon.MediaType, Is.EqualTo("image/svg+xml"));
			Assert.That(_store.TryGet(icon.ResourceId, out var served), Is.True);
			Assert.That(served.Content.Length, Is.GreaterThan(0));
		});
	}

	[Test]
	public async Task A_bundled_icon_in_a_folder_whose_name_is_not_an_identifier_is_found()
	{
		var icon = await Create().GetPluginIconAsync("status-badges", "online");

		Assert.That(icon.MediaType, Is.EqualTo("image/png"));
	}

	[Test]
	public async Task A_bundled_icon_keeps_its_handle_across_calls_and_instances()
	{
		var first = await Create().GetPluginIconAsync("widget", "shield");
		var second = await Create().GetPluginIconAsync("widget", "shield");

		Assert.That(second, Is.EqualTo(first));
	}

	[TestCase("widget", "missing")]
	[TestCase("missing", "shield")]
	[TestCase("..", "shield")]
	public void An_unknown_bundled_icon_is_not_found(string key, string name)
	{
		var exception = Assert.ThrowsAsync<UiResourceException>(() => Create().GetPluginIconAsync(key, name));

		Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.PluginIconNotFound));
	}

	[Test]
	public async Task A_registered_name_can_never_replace_or_remove_a_bundled_icon()
	{
		var resources = Create();
		var icon = await resources.GetPluginIconAsync("widget", "shield");

		await resources.RegisterAsync("icon", _png, "image/png");
		await resources.RemoveAsync("icon");

		Assert.That(_store.TryGet(icon.ResourceId, out var served) && served.MediaType == "image/svg+xml", Is.True);
	}

	[Test]
	public async Task Releasing_removes_every_resource_and_refuses_further_registrations()
	{
		var resources = Create();
		var image = await resources.RegisterAsync("cover", _png, "image/png");
		var icon = await resources.GetPluginIconAsync("widget", "shield");

		resources.Release();

		var exception
			= Assert.ThrowsAsync<UiResourceException>(() => resources.RegisterAsync("cover", _png, "image/png"));
		Assert.Multiple(() =>
		{
			Assert.That(_store.TryGet(image.ResourceId, out _), Is.False);
			Assert.That(_store.TryGet(icon.ResourceId, out _), Is.False);
			Assert.That(exception!.ErrorCode, Is.EqualTo(UiResourceErrorCode.Failed));
		});
	}

	[Test]
	public async Task Another_players_artwork_is_registered_like_any_other_image()
	{
		var resources = Create(new StubArtwork(new ArtworkImageResult(_png, "image/png", "etag")));

		var handle = await resources.RegisterMusicPlayerArtworkAsync("now-playing",
			"com.example.player::default",
			"a1");
		var missing = await Create(new StubArtwork(null))
			.RegisterMusicPlayerArtworkAsync("now-playing", "com.example.player::default", "gone");

		Assert.Multiple(() =>
		{
			Assert.That(handle!.ResourceId, Is.EqualTo(IntegrationId + ".now-playing"));
			Assert.That(missing, Is.Null);
		});
	}

	[Test]
	public async Task Another_players_artwork_asked_for_at_a_size_is_registered_as_the_copy_for_that_size()
	{
		var artwork = new SizedArtwork();
		var resources = Create(artwork);

		var small = await resources.RegisterMusicPlayerArtworkAsync("small", "com.example.player::default", "a1", 256);
		var full = await resources.RegisterMusicPlayerArtworkAsync("full", "com.example.player::default", "a1");

		Assert.Multiple(() =>
		{
			Assert.That(artwork.Sizes, Is.EqualTo(new int?[] { 256, null }));
			Assert.That(small!.ByteLength, Is.EqualTo(SizedArtwork.CopyLength));
			Assert.That(full!.ByteLength, Is.EqualTo(SizedArtwork.FullLength));
		});
	}

	[Test]
	public void An_artwork_size_that_is_not_positive_is_refused()
	{
		var resources = Create(new SizedArtwork());

		Assert.Throws<ArgumentOutOfRangeException>(() =>
			_ = resources.RegisterMusicPlayerArtworkAsync("cover", "com.example.player::default", "a1", 0));
	}

	private sealed class SizedArtwork : IMusicPlayerArtworkService
	{
		public const int CopyLength = 4;
		public const int FullLength = 9;

		public List<int?> Sizes { get; } = [];

		public string GetETag(string artworkId, int? size)
		{
			return "etag";
		}

		public Task<ArtworkImageResult?> GetImage(
			string instanceId,
			string artworkId,
			int? size,
			CancellationToken cancellationToken)
		{
			Sizes.Add(size);
			return Task.FromResult<ArtworkImageResult?>(
				new ArtworkImageResult(new byte[size is null ? FullLength : CopyLength], "image/webp", "etag"));
		}

		public Task<ArtworkImageResult?> GetImage(
			MusicPlayerVariant variant,
			string artworkId,
			int? size,
			CancellationToken cancellationToken)
		{
			throw new NotSupportedException();
		}
	}

	private sealed class StubArtwork(ArtworkImageResult? image) : IMusicPlayerArtworkService
	{
		public string GetETag(string artworkId, int? size)
		{
			return "etag";
		}

		public Task<ArtworkImageResult?> GetImage(
			string instanceId,
			string artworkId,
			int? size,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(image);
		}

		public Task<ArtworkImageResult?> GetImage(
			MusicPlayerVariant variant,
			string artworkId,
			int? size,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(image);
		}
	}
}
