using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Ui.Resources;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Ui.Resources;

[TestFixture]
public class PluginUiResourcesTests
{
	private const string PluginA = "com.example.photos";
	private const string PluginB = "com.example.other";

	private static readonly byte[] _first = [1, 2, 3];
	private static readonly byte[] _second = [4, 5, 6, 7];

	private PluginSessionRegistry _sessions = null!;
	private InMemoryPluginAssetCache _cache = null!;
	private PluginAssetReceiver _assets = null!;
	private UiResourceStore _store = null!;
	private PluginUiResources _resources = null!;
	private int _assetIds;

	[SetUp]
	public void SetUp()
	{
		_sessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_cache = new InMemoryPluginAssetCache();
		_assets = new PluginAssetReceiver(_cache);
		_store = new UiResourceStore();
		_resources = new PluginUiResources(_store, _assets, _sessions);
	}

	[TearDown]
	public void TearDown() => _resources.Dispose();

	[Test]
	public async Task Uploaded_bytes_registered_under_a_name_are_served_with_their_media_type()
	{
		await StartSessionAsync(PluginA, "s1");
		var hash = Upload(PluginA, _first);

		var result = _resources.Register(PluginA, "s1", "photo", hash, "image/PNG");

		Assert.That(result.Outcome, Is.EqualTo(PluginUiResourceOutcome.Registered));
		Assert.That(_store.TryGet(result.Resource!.ResourceId, out var served), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(served.Content.ToArray(), Is.EqualTo(_first));
			Assert.That(served.MediaType, Is.EqualTo("image/png"));
			Assert.That(result.Resource.ContentHash, Is.EqualTo(hash));
		});
	}

	[Test]
	public async Task Bytes_that_were_never_uploaded_ask_for_an_upload()
	{
		await StartSessionAsync(PluginA, "s1");

		var result = _resources.Register(PluginA, "s1", "photo", AssetContentHash.Compute(_first), "image/png");

		Assert.That(result.Outcome, Is.EqualTo(PluginUiResourceOutcome.UploadRequired));
	}

	[Test]
	public async Task Bytes_another_plugin_uploaded_cannot_be_registered()
	{
		await StartSessionAsync(PluginA, "s1");
		await StartSessionAsync(PluginB, "s2");
		var hash = Upload(PluginB, _first);

		var result = _resources.Register(PluginA, "s1", "photo", hash, "image/png");

		Assert.That(result.Outcome, Is.EqualTo(PluginUiResourceOutcome.UploadRequired));
	}

	[Test]
	public async Task Registering_a_name_again_replaces_the_bytes_under_the_same_id()
	{
		await StartSessionAsync(PluginA, "s1");
		var first = _resources.Register(PluginA, "s1", "photo", Upload(PluginA, _first), "image/png").Resource!;

		var second = _resources.Register(PluginA, "s1", "photo", Upload(PluginA, _second), "image/png").Resource!;

		_store.TryGet(second.ResourceId, out var served);
		Assert.Multiple(() =>
		{
			Assert.That(second.ResourceId, Is.EqualTo(first.ResourceId));
			Assert.That(second.ContentHash, Is.Not.EqualTo(first.ContentHash));
			Assert.That(served.Content.ToArray(), Is.EqualTo(_second));
		});
	}

	[Test]
	public async Task Registering_the_same_bytes_under_the_same_name_needs_no_new_upload()
	{
		await StartSessionAsync(PluginA, "s1");
		var hash = Upload(PluginA, _first);
		var first = _resources.Register(PluginA, "s1", "photo", hash, "image/png").Resource;

		var again = _resources.Register(PluginA, "s1", "photo", hash, "image/png");

		Assert.Multiple(() =>
		{
			Assert.That(again.Outcome, Is.EqualTo(PluginUiResourceOutcome.Registered));
			Assert.That(again.Resource, Is.EqualTo(first));
		});
	}

	[Test]
	public async Task Two_plugins_using_the_same_name_get_separate_resources()
	{
		await StartSessionAsync(PluginA, "s1");
		await StartSessionAsync(PluginB, "s2");

		var a = _resources.Register(PluginA, "s1", "photo", Upload(PluginA, _first), "image/png").Resource!;
		var b = _resources.Register(PluginB, "s2", "photo", Upload(PluginB, _second), "image/png").Resource!;

		_store.TryGet(a.ResourceId, out var servedA);
		Assert.Multiple(() =>
		{
			Assert.That(a.ResourceId, Is.Not.EqualTo(b.ResourceId));
			Assert.That(servedA.Content.ToArray(), Is.EqualTo(_first));
			Assert.That(a.ResourceId, Does.Not.StartWith("app.macro-deck."));
		});
	}

	[Test]
	public async Task A_plugin_past_its_resource_count_is_refused_and_keeps_what_it_had()
	{
		await StartSessionAsync(PluginA, "s1");
		for (var index = 0; index < ProtocolLimits.MaxUiResourcesPerPlugin; index++)
		{
			Assert.That(RegisterSmall(index).Outcome, Is.EqualTo(PluginUiResourceOutcome.Registered));
		}

		var refused = _resources.Register(PluginA, "s1", "one-too-many", Upload(PluginA, _second), "image/png");
		var replaced = _resources.Register(PluginA, "s1", "photo-0", Upload(PluginA, _second), "image/png");

		Assert.Multiple(() =>
		{
			Assert.That(refused.Outcome, Is.EqualTo(PluginUiResourceOutcome.QuotaExceeded));
			Assert.That(replaced.Outcome, Is.EqualTo(PluginUiResourceOutcome.Registered),
				"replacing a name does not add to the count");
		});
	}

	[Test]
	public async Task A_plugin_past_its_byte_quota_is_refused_and_the_name_keeps_its_bytes()
	{
		await StartSessionAsync(PluginA, "s1");
		var perResource = ProtocolLimits.MaxUiResourceBytes;
		var fits = ProtocolLimits.MaxUiResourceBytesPerPlugin / perResource;
		for (var index = 0; index < fits; index++)
		{
			_resources.Register(PluginA,
				"s1",
				$"photo-{index}",
				Upload(PluginA, Filled(perResource, index)),
				"image/png");
		}

		var before = _resources
			.Register(PluginA, "s1", "photo-0", AssetContentHash.Compute(Filled(perResource, 0)), "image/png")
			.Resource;
		var refused = _resources.Register(PluginA, "s1", "extra", Upload(PluginA, _first), "image/png");

		Assert.Multiple(() =>
		{
			Assert.That(refused.Outcome, Is.EqualTo(PluginUiResourceOutcome.QuotaExceeded));
			Assert.That(_store.TryGet(before!.ResourceId, out var kept), Is.True);
			Assert.That(kept.ContentHash, Is.EqualTo(before.ContentHash));
		});
	}

	[Test]
	public async Task Removing_a_resource_stops_serving_it_and_frees_its_share_of_the_quota()
	{
		await StartSessionAsync(PluginA, "s1");
		for (var index = 0; index < ProtocolLimits.MaxUiResourcesPerPlugin; index++)
		{
			RegisterSmall(index);
		}

		var removedId = RegisterSmall(0).Resource!.ResourceId;

		var removal = _resources.Remove(PluginA, "s1", "photo-0");
		var unknown = _resources.Remove(PluginA, "s1", "never-registered");
		var next = _resources.Register(PluginA, "s1", "new", Upload(PluginA, _first), "image/png");

		Assert.Multiple(() =>
		{
			Assert.That(removal.Outcome, Is.EqualTo(PluginUiResourceOutcome.Removed));
			Assert.That(unknown.Outcome, Is.EqualTo(PluginUiResourceOutcome.Removed));
			Assert.That(_store.TryGet(removedId, out _), Is.False);
			Assert.That(next.Outcome, Is.EqualTo(PluginUiResourceOutcome.Registered));
		});
	}

	[TestCase("has.dot", "image/png", PluginUiResourceOutcome.InvalidName)]
	[TestCase("", "image/png", PluginUiResourceOutcome.InvalidName)]
	[TestCase("photo", "image/svg+xml", PluginUiResourceOutcome.UnsupportedMediaType)]
	[TestCase("photo", "image/jpeg", PluginUiResourceOutcome.MediaTypeMismatch)]
	public async Task An_unusable_registration_is_refused(string name, string mediaType, PluginUiResourceOutcome expected)
	{
		await StartSessionAsync(PluginA, "s1");
		var hash = Upload(PluginA, _first, "image/png");

		Assert.That(_resources.Register(PluginA, "s1", name, hash, mediaType).Outcome, Is.EqualTo(expected));
	}

	[Test]
	public async Task A_resumable_disconnect_keeps_the_resources()
	{
		await StartSessionAsync(PluginA, "s1");
		var handle = _resources.Register(PluginA, "s1", "photo", Upload(PluginA, _first), "image/png").Resource!;

		_sessions.Detach("s1", TimeProvider.System.GetUtcNow());

		Assert.That(_store.TryGet(handle.ResourceId, out _), Is.True);
	}

	[Test]
	public async Task Ending_the_plugin_session_releases_its_resources()
	{
		await StartSessionAsync(PluginA, "s1");
		var handle = _resources.Register(PluginA, "s1", "photo", Upload(PluginA, _first), "image/png").Resource!;

		await _sessions.TerminateForPlugin(PluginA, 1000, "stopped");

		Assert.That(_store.TryGet(handle.ResourceId, out _), Is.False);
	}

	[Test]
	public async Task A_registration_from_a_replaced_session_is_refused_and_takes_no_quota()
	{
		await StartSessionAsync(PluginA, "s1");
		var hash = Upload(PluginA, _first);
		var old = _resources.Register(PluginA, "s1", "photo", hash, "image/png").Resource!;

		await StartSessionAsync(PluginA, "s2");
		var late = _resources.Register(PluginA, "s1", "late", Upload(PluginA, _second), "image/png");

		Assert.Multiple(() =>
		{
			Assert.That(late.Outcome, Is.EqualTo(PluginUiResourceOutcome.SessionNotCurrent));
			Assert.That(_store.TryGet(old.ResourceId, out _), Is.False, "the replaced session's resources are released");
			Assert.That(_store.TryGet(PluginUiResources.OwnerId(PluginA) + ".late", out _), Is.False);
		});
	}

	[Test]
	public void An_ui_resource_upload_is_not_written_to_the_shared_disk_cache()
	{
		var hash = Upload(PluginA, _first);

		Assert.That(_cache.TryRead(hash, out _, out _), Is.False);
	}

	private async Task StartSessionAsync(string pluginId, string sessionId)
	{
		await _sessions.Create(new PluginSessionRecord
		{
			SessionId = sessionId,
			PluginId = pluginId,
			DisplayName = pluginId,
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 3,
			Capabilities = new Dictionary<string, MacroDeck.Plugin.Protocol.Versioning.CapabilityNegotiationResult>(),
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow(),
		});
		_sessions.TryAttach(sessionId, new FakePluginConnection(), null);
	}

	private string Upload(string pluginId, byte[] bytes, string mimeType = "image/png")
	{
		var assetId = $"asset-{++_assetIds}";
		var hash = AssetContentHash.Compute(bytes);

		Assert.That(_assets.Begin(pluginId, assetId, AssetKinds.UiResource, mimeType, bytes.Length, hash).Accepted,
			Is.True);
		for (var offset = 0; offset < bytes.Length; offset += ProtocolLimits.MaxAssetChunkBytes)
		{
			var chunk = bytes.AsSpan(offset, Math.Min(ProtocolLimits.MaxAssetChunkBytes, bytes.Length - offset));
			Assert.That(_assets.Chunk(pluginId, assetId, offset / ProtocolLimits.MaxAssetChunkBytes, chunk).Accepted,
				Is.True);
		}

		Assert.That(_assets.Commit(pluginId, assetId).Accepted, Is.True);

		return hash;
	}

	private PluginUiResourceResult RegisterSmall(int index)
		=> _resources.Register(PluginA,
			"s1",
			$"photo-{index}",
			Upload(PluginA, [(byte)(index % 256), (byte)(index / 256)]),
			"image/png");

	private static byte[] Filled(int length, int seed)
	{
		var bytes = new byte[length];
		bytes[0] = (byte)seed;
		bytes[1] = (byte)(seed >> 8);
		return bytes;
	}
}
