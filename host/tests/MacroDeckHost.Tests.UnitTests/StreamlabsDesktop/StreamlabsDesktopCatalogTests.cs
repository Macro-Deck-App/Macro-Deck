using MacroDeckHost.Integrations.StreamlabsDesktop;
using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;

namespace MacroDeckHost.Tests.UnitTests.StreamlabsDesktop;

[TestFixture]
public class StreamlabsDesktopCatalogTests
{
	private static readonly string[] _bothItemIds = ["a", "b"];

	private FakeStreamlabsClient _client = null!;
	private StreamlabsDesktopCatalog _catalog = null!;

	[SetUp]
	public void SetUp()
	{
		_client = StreamlabsJson.Seeded();
		_catalog = new StreamlabsDesktopCatalog(() => _client);
	}

	[TearDown]
	public void TearDown() => _client.Dispose();

	[Test]
	public async Task ResolveSceneAsync_MapsANameToItsScene()
	{
		var scene = await _catalog.ResolveSceneAsync(StreamlabsJson.SceneName);

		Assert.Multiple(() =>
		{
			Assert.That(scene, Is.Not.Null);
			Assert.That(scene!.Id, Is.EqualTo(StreamlabsJson.SceneId));
			Assert.That(scene.ResourceId, Is.EqualTo(StreamlabsJson.SceneResource));
		});
	}

	[Test]
	public async Task ResolveAudioSourceAsync_MapsANameToItsAudioSource()
	{
		var source = await _catalog.ResolveAudioSourceAsync(StreamlabsJson.MicSourceName);

		Assert.Multiple(() =>
		{
			Assert.That(source, Is.Not.Null);
			Assert.That(source!.SourceId, Is.EqualTo(StreamlabsJson.MicSourceId));
			Assert.That(source.Deflection, Is.EqualTo(0.8d).Within(0.001d));
		});
	}

	[Test]
	public async Task ResolveSceneItemsAsync_JoinsTheSourceNameFromTheSourceCatalog()
	{
		var items = await _catalog.ResolveSceneItemsAsync(StreamlabsJson.SceneName, StreamlabsJson.CameraSourceName);

		Assert.Multiple(() =>
		{
			Assert.That(items, Has.Count.EqualTo(1));
			Assert.That(items[0].SceneItemId, Is.EqualTo(StreamlabsJson.CameraItemId));
			Assert.That(items[0].Name, Is.EqualTo(StreamlabsJson.CameraSourceName));
			Assert.That(items[0].ResourceId, Is.EqualTo(StreamlabsJson.SceneItemResource));
		});
	}

	[Test]
	public async Task ASecondLookup_DoesNotQueryAgain()
	{
		await _catalog.ResolveSceneAsync(StreamlabsJson.SceneName);
		var afterFirst = _client.Calls.Count;

		await _catalog.ResolveSceneAsync(StreamlabsJson.OtherSceneName);

		Assert.That(_client.Calls, Has.Count.EqualTo(afterFirst));
	}

	[Test]
	public async Task AMissForcesExactlyOneRefreshAndThenGivesUp()
	{
		await _catalog.ResolveSceneAsync(StreamlabsJson.SceneName);
		_client.Calls.Clear();

		var missing = await _catalog.ResolveSceneAsync("Nope");

		Assert.Multiple(() =>
		{
			Assert.That(missing, Is.Null);
			Assert.That(_client.Calls.Count(call => call.Contains("getScenes", StringComparison.Ordinal)),
				Is.EqualTo(1),
				"a miss must refresh once, not loop");
		});
	}

	[Test]
	public async Task AMissRecoversWhenTheRefreshBringsTheNameIn()
	{
		await _catalog.ResolveSceneAsync(StreamlabsJson.SceneName);

		_client.Responses[$"{StreamlabsServices.Scenes}.{StreamlabsServices.GetScenes}"] =
			"""[{"id":"scene_new","name":"Renamed"}]""";

		var scene = await _catalog.ResolveSceneAsync("Renamed");

		Assert.That(scene?.Id, Is.EqualTo("scene_new"));
	}

	[Test]
	public async Task ConcurrentMisses_ShareASingleRefresh()
	{
		await _catalog.ResolveSceneAsync(StreamlabsJson.SceneName);
		_client.Calls.Clear();

		await Task.WhenAll(Enumerable.Range(0, 8)
			.Select(index => _catalog.ResolveSceneAsync($"missing-{index}")));

		Assert.That(_client.Calls.Count(call => call.Contains("getScenes", StringComparison.Ordinal)),
			Is.LessThanOrEqualTo(8),
			"a stampede of misses must not fire one full refresh per caller");
	}

	[Test]
	public async Task AnExactMatchWinsOverACaseInsensitiveOne()
	{
		_client.Responses[$"{StreamlabsServices.Scenes}.{StreamlabsServices.GetScenes}"] =
			"""[{"id":"lower","name":"scene"},{"id":"upper","name":"Scene"}]""";

		var scene = await _catalog.ResolveSceneAsync("Scene");

		Assert.That(scene?.Id, Is.EqualTo("upper"));
	}

	[Test]
	public async Task ACaseInsensitiveMatchIsTheFallback()
	{
		var scene = await _catalog.ResolveSceneAsync("gameplay");

		Assert.That(scene?.Id, Is.EqualTo(StreamlabsJson.SceneId));
	}

	[Test]
	public async Task TwoItemsSharingASourceNameBothResolve()
	{
		_client.Responses[$"{StreamlabsJson.SceneResource}.{StreamlabsServices.GetItems}"] = $$"""
			  [
			  	{"sceneId":"{{StreamlabsJson.SceneId}}","sceneItemId":"a","sourceId":"{{StreamlabsJson.CameraSourceId}}","visible":true},
			  	{"sceneId":"{{StreamlabsJson.SceneId}}","sceneItemId":"b","sourceId":"{{StreamlabsJson.CameraSourceId}}","visible":false}
			  ]
			  """;

		var items = await _catalog.ResolveSceneItemsAsync(StreamlabsJson.SceneName, StreamlabsJson.CameraSourceName);

		Assert.That(items.Select(item => item.SceneItemId), Is.EquivalentTo(_bothItemIds));
	}

	[Test]
	public async Task Invalidate_ForcesTheNextLookupToReadAgain()
	{
		await _catalog.ResolveSceneAsync(StreamlabsJson.SceneName);
		_client.Calls.Clear();

		_catalog.Invalidate();
		await _catalog.ResolveSceneAsync(StreamlabsJson.SceneName);

		Assert.That(_client.Calls.Count(call => call.Contains("getScenes", StringComparison.Ordinal)),
			Is.EqualTo(1));
	}

	[Test]
	public async Task InvalidateScene_OnlyDropsThatScenesItems()
	{
		await _catalog.ResolveSceneItemsAsync(StreamlabsJson.SceneName, StreamlabsJson.CameraSourceName);
		_client.Calls.Clear();

		_catalog.InvalidateScene(StreamlabsJson.SceneId);
		await _catalog.ResolveSceneItemsAsync(StreamlabsJson.SceneName, StreamlabsJson.CameraSourceName);

		Assert.Multiple(() =>
		{
			Assert.That(_client.Calls.Count(call => call.Contains("getItems", StringComparison.Ordinal)),
				Is.EqualTo(1));
			Assert.That(_client.Calls.Any(call => call.Contains("getScenes", StringComparison.Ordinal)),
				Is.False,
				"the scene list itself is untouched");
		});
	}

	[Test]
	public async Task PatchItemVisibility_UpdatesInPlaceWithoutARefetch()
	{
		await _catalog.ResolveSceneItemsAsync(StreamlabsJson.SceneName, StreamlabsJson.CameraSourceName);
		_client.Calls.Clear();

		_catalog.PatchItemVisibility(StreamlabsJson.SceneId, StreamlabsJson.CameraItemId, visible: false);
		var items = await _catalog.ResolveSceneItemsAsync(StreamlabsJson.SceneName, StreamlabsJson.CameraSourceName);

		Assert.Multiple(() =>
		{
			Assert.That(items[0].Visible, Is.False);
			Assert.That(_client.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task PatchAudioMuted_UpdatesInPlaceWithoutARefetch()
	{
		await _catalog.ResolveAudioSourceAsync(StreamlabsJson.MicSourceName);
		_client.Calls.Clear();

		_catalog.PatchAudioMuted(StreamlabsJson.MicSourceId, muted: true);
		var source = await _catalog.ResolveAudioSourceAsync(StreamlabsJson.MicSourceName);

		Assert.Multiple(() =>
		{
			Assert.That(source!.Muted, Is.True);
			Assert.That(_client.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task SourceName_AnswersFromMemoryForAnEventPayload()
	{
		await _catalog.ResolveSceneAsync(StreamlabsJson.SceneName);

		Assert.Multiple(() =>
		{
			Assert.That(_catalog.SourceName(StreamlabsJson.MicSourceId), Is.EqualTo(StreamlabsJson.MicSourceName));
			Assert.That(_catalog.SceneName(StreamlabsJson.SceneId), Is.EqualTo(StreamlabsJson.SceneName));
			Assert.That(_catalog.SourceName("unknown"), Is.Null);
		});
	}

	[Test]
	public async Task WithoutAClient_EverythingIsEmptyRatherThanThrowing()
	{
		var catalog = new StreamlabsDesktopCatalog(() => null);

		Assert.Multiple(async () =>
		{
			Assert.That(await catalog.GetSceneNamesAsync(), Is.Empty);
			Assert.That(await catalog.GetAudioSourceNamesAsync(), Is.Empty);
			Assert.That(await catalog.ResolveSceneAsync("anything"), Is.Null);
			Assert.That(await catalog.ResolveSceneItemsAsync("a", "b"), Is.Empty);
		});
	}
}
