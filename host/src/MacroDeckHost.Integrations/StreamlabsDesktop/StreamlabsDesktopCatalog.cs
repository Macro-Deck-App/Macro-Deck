using System.Text.Json;
using MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.StreamlabsDesktop;

internal sealed class StreamlabsDesktopCatalog
{
	private static readonly ILogger _logger =
		IntegrationLog.For<StreamlabsDesktopCatalog>(StreamlabsDesktopIntegration.IntegrationId);

	private static readonly TimeSpan _defaultTimeToLive = TimeSpan.FromSeconds(60);

	private readonly Func<IStreamlabsClient?> _client;
	private readonly TimeSpan _timeToLive;
	private readonly object _gate = new();

	private Catalog? _current;
	private Task<Catalog>? _loading;

	private Dictionary<string, IReadOnlyList<StreamlabsSceneItem>> _items =
		new(StringComparer.Ordinal);

	internal StreamlabsDesktopCatalog(Func<IStreamlabsClient?> client, TimeSpan? timeToLive = null)
	{
		_client = client;
		_timeToLive = timeToLive ?? _defaultTimeToLive;
	}

	public async Task<IReadOnlyList<string>> GetSceneNamesAsync()
	{
		var catalog = await GetAsync(forceRefresh: false).ConfigureAwait(false);
		return catalog?.Scenes.Select(scene => scene.Name).ToList() ?? [];
	}

	public async Task<IReadOnlyList<string>> GetAudioSourceNamesAsync()
	{
		var catalog = await GetAsync(forceRefresh: false).ConfigureAwait(false);
		return catalog?.AudioSources.Select(source => source.Name).ToList() ?? [];
	}

	public async Task<IReadOnlyList<string>> GetSceneItemNamesAsync(string sceneName)
	{
		var scene = await ResolveSceneAsync(sceneName).ConfigureAwait(false);
		if (scene is null)
		{
			return [];
		}

		var items = await GetItemsAsync(scene, forceRefresh: false).ConfigureAwait(false);
		return items.Select(item => item.Name).Distinct(StringComparer.Ordinal).ToList();
	}

	public async Task<StreamlabsScene?> ResolveSceneAsync(string name)
	{
		var catalog = await GetAsync(forceRefresh: false).ConfigureAwait(false);
		if (catalog is not null && Lookup(catalog.ScenesByName, name) is { } hit)
		{
			return hit;
		}

		catalog = await GetAsync(forceRefresh: true).ConfigureAwait(false);
		return catalog is null ? null : Lookup(catalog.ScenesByName, name);
	}

	public async Task<StreamlabsAudioSource?> ResolveAudioSourceAsync(string name)
	{
		var catalog = await GetAsync(forceRefresh: false).ConfigureAwait(false);
		if (catalog is not null && Lookup(catalog.AudioSourcesByName, name) is { } hit)
		{
			return hit;
		}

		catalog = await GetAsync(forceRefresh: true).ConfigureAwait(false);
		return catalog is null ? null : Lookup(catalog.AudioSourcesByName, name);
	}

	public async Task<IReadOnlyList<StreamlabsSceneItem>> ResolveSceneItemsAsync(
		string sceneName,
		string sourceName)
	{
		var scene = await ResolveSceneAsync(sceneName).ConfigureAwait(false);
		if (scene is null)
		{
			return [];
		}

		var items = Match(await GetItemsAsync(scene, forceRefresh: false).ConfigureAwait(false), sourceName);
		if (items.Count > 0)
		{
			return items;
		}

		return Match(await GetItemsAsync(scene, forceRefresh: true).ConfigureAwait(false), sourceName);
	}

	public string? SourceName(string sourceId)
	{
		var catalog = _current;
		return catalog is not null && catalog.SourceNamesById.TryGetValue(sourceId, out var name) ? name : null;
	}

	public string? SceneName(string sceneId)
		=> _current?.Scenes.FirstOrDefault(scene => string.Equals(scene.Id, sceneId, StringComparison.Ordinal))?.Name;

	public StreamlabsSceneItem? FindItem(string sceneId, string sceneItemId)
	{
		lock (_gate)
		{
			return _items.TryGetValue(sceneId, out var items)
				? items.FirstOrDefault(item =>
					string.Equals(item.SceneItemId, sceneItemId, StringComparison.Ordinal))
				: null;
		}
	}

	public void PatchItemVisibility(string sceneId, string sceneItemId, bool visible)
	{
		lock (_gate)
		{
			if (!_items.TryGetValue(sceneId, out var items))
			{
				return;
			}

			_items[sceneId] = items
				.Select(item => string.Equals(item.SceneItemId, sceneItemId, StringComparison.Ordinal)
					? item with { Visible = visible }
					: item)
				.ToList();
		}
	}

	public void PatchAudioMuted(string sourceId, bool muted)
	{
		lock (_gate)
		{
			if (_current is not { } catalog)
			{
				return;
			}

			var updated = catalog.AudioSources
				.Select(source => string.Equals(source.SourceId, sourceId, StringComparison.Ordinal)
					? source with { Muted = muted }
					: source)
				.ToList();

			_current = Catalog.Create(catalog.Scenes, catalog.SourceNamesById, updated);
		}
	}

	public void Invalidate()
	{
		lock (_gate)
		{
			_current = null;
			_items = new Dictionary<string, IReadOnlyList<StreamlabsSceneItem>>(StringComparer.Ordinal);
		}
	}

	public void InvalidateScene(string sceneId)
	{
		lock (_gate)
		{
			_items.Remove(sceneId);
		}
	}

	public void Clear() => Invalidate();

	private static List<StreamlabsSceneItem> Match(
		IReadOnlyList<StreamlabsSceneItem> items,
		string sourceName)
	{
		var exact = items
			.Where(item => string.Equals(item.Name, sourceName, StringComparison.Ordinal))
			.ToList();

		return exact.Count > 0
			? exact
			: items
				.Where(item => string.Equals(item.Name, sourceName, StringComparison.OrdinalIgnoreCase))
				.ToList();
	}

	private static T? Lookup<T>(IReadOnlyDictionary<string, T> map, string name)
		where T : class
	{
		if (map.TryGetValue(name, out var exact))
		{
			return exact;
		}

		foreach (var pair in map)
		{
			if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase))
			{
				return pair.Value;
			}
		}

		return null;
	}

	private async Task<IReadOnlyList<StreamlabsSceneItem>> GetItemsAsync(
		StreamlabsScene scene,
		bool forceRefresh)
	{
		if (!forceRefresh)
		{
			lock (_gate)
			{
				if (_items.TryGetValue(scene.Id, out var cached))
				{
					return cached;
				}
			}
		}

		if (_client() is not { } client)
		{
			return [];
		}

		try
		{
			var response = await client
				.InvokeAsync(scene.ResourceId, StreamlabsServices.GetItems, null, CancellationToken.None)
				.ConfigureAwait(false);

			var items = ReadItems(response, scene.Id);
			lock (_gate)
			{
				_items[scene.Id] = items;
			}

			return items;
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not read the items of Streamlabs scene {Scene}", scene.Name);
			return [];
		}
	}

	private List<StreamlabsSceneItem> ReadItems(JsonElement response, string sceneId)
	{
		if (response.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var items = new List<StreamlabsSceneItem>();
		foreach (var element in response.EnumerateArray())
		{
			if (StreamlabsModelReader.ReadSceneItem(element, sceneId, SourceName) is { } item)
			{
				items.Add(item);
			}
		}

		return items;
	}

	private Task<Catalog> BeginLoad(bool forceRefresh)
	{
		lock (_gate)
		{
			if (!forceRefresh && _current is { } current && DateTimeOffset.UtcNow - current.LoadedAt < _timeToLive)
			{
				return Task.FromResult(current);
			}

			if (_loading is { } running)
			{
				return running;
			}

			var load = LoadAsync();
			_loading = load;

			_ = load.ContinueWith(_ =>
				{
					lock (_gate)
					{
						if (ReferenceEquals(_loading, load))
						{
							_loading = null;
						}
					}
				},
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);

			return load;
		}
	}

	private async Task<Catalog?> GetAsync(bool forceRefresh)
	{
		if (_client() is null)
		{
			return _current;
		}

		try
		{
			return await BeginLoad(forceRefresh).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not refresh the Streamlabs Desktop catalog");
			return _current;
		}
	}

	private async Task<Catalog> LoadAsync()
	{
		if (_client() is not { } client)
		{
			return Catalog.Empty;
		}

		var scenes = ReadScenes(await client
			.InvokeAsync(StreamlabsServices.Scenes, StreamlabsServices.GetScenes, null, CancellationToken.None)
			.ConfigureAwait(false));

		var sources = ReadSources(await client
			.InvokeAsync(StreamlabsServices.Sources, StreamlabsServices.GetSources, null, CancellationToken.None)
			.ConfigureAwait(false));

		var audio = ReadAudioSources(await client
			.InvokeAsync(StreamlabsServices.Audio, StreamlabsServices.GetSources, null, CancellationToken.None)
			.ConfigureAwait(false));

		var catalog = Catalog.Create(scenes, sources, audio);
		lock (_gate)
		{
			_current = catalog;
			_items = new Dictionary<string, IReadOnlyList<StreamlabsSceneItem>>(StringComparer.Ordinal);
		}

		return catalog;
	}

	private static List<StreamlabsScene> ReadScenes(JsonElement response)
	{
		if (response.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var scenes = new List<StreamlabsScene>();
		foreach (var element in response.EnumerateArray())
		{
			if (StreamlabsModelReader.ReadScene(element) is { } scene)
			{
				scenes.Add(scene);
			}
		}

		return scenes;
	}

	private static Dictionary<string, string> ReadSources(JsonElement response)
	{
		var names = new Dictionary<string, string>(StringComparer.Ordinal);
		if (response.ValueKind != JsonValueKind.Array)
		{
			return names;
		}

		foreach (var element in response.EnumerateArray())
		{
			if (StreamlabsModelReader.ReadSource(element) is { } source)
			{
				names[source.SourceId] = source.Name;
			}
		}

		return names;
	}

	private static List<StreamlabsAudioSource> ReadAudioSources(JsonElement response)
	{
		if (response.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var sources = new List<StreamlabsAudioSource>();
		foreach (var element in response.EnumerateArray())
		{
			if (StreamlabsModelReader.ReadAudioSource(element) is { } source)
			{
				sources.Add(source);
			}
		}

		return sources;
	}

	private sealed record Catalog(
		IReadOnlyList<StreamlabsScene> Scenes,
		IReadOnlyDictionary<string, StreamlabsScene> ScenesByName,
		IReadOnlyDictionary<string, string> SourceNamesById,
		IReadOnlyList<StreamlabsAudioSource> AudioSources,
		IReadOnlyDictionary<string, StreamlabsAudioSource> AudioSourcesByName,
		DateTimeOffset LoadedAt)
	{
		public static Catalog Empty { get; } = Create([], new Dictionary<string, string>(StringComparer.Ordinal), []);

		public static Catalog Create(
			IReadOnlyList<StreamlabsScene> scenes,
			IReadOnlyDictionary<string, string> sourceNamesById,
			IReadOnlyList<StreamlabsAudioSource> audioSources)
		{
			var scenesByName = new Dictionary<string, StreamlabsScene>(StringComparer.Ordinal);
			foreach (var scene in scenes)
			{
				scenesByName[scene.Name] = scene;
			}

			var audioByName = new Dictionary<string, StreamlabsAudioSource>(StringComparer.Ordinal);
			foreach (var source in audioSources)
			{
				audioByName[source.Name] = source;
			}

			return new Catalog(scenes,
				scenesByName,
				sourceNamesById,
				audioSources,
				audioByName,
				DateTimeOffset.UtcNow);
		}
	}
}
