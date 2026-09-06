using System.Text.Json;

namespace MacroDeckHost.Integrations.Meld;

internal static class MeldSessionParser
{
	public static MeldSession Parse(JsonElement session)
	{
		if (session.ValueKind != JsonValueKind.Object ||
			!session.TryGetProperty("items", out var itemsElement) ||
			itemsElement.ValueKind != JsonValueKind.Object)
		{
			return MeldSession.Empty;
		}

		var rawScenes = new Dictionary<string, RawScene>(StringComparer.Ordinal);
		var rawLayers = new Dictionary<string, RawLayer>(StringComparer.Ordinal);
		var rawEffects = new Dictionary<string, RawEffect>(StringComparer.Ordinal);
		var rawTracks = new Dictionary<string, RawTrack>(StringComparer.Ordinal);

		foreach (var item in itemsElement.EnumerateObject())
		{
			if (item.Value.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			switch (ReadString(item.Value, "type"))
			{
				case "scene":
					if (TryParseScene(item.Value, out var scene))
					{
						rawScenes[item.Name] = scene;
					}

					break;
				case "layer":
					if (TryParseLayer(item.Value, out var layer))
					{
						rawLayers[item.Name] = layer;
					}

					break;
				case "effect":
					if (TryParseEffect(item.Value, out var effect))
					{
						rawEffects[item.Name] = effect;
					}

					break;
				case "track":
					if (TryParseTrack(item.Value, out var track))
					{
						rawTracks[item.Name] = track;
					}

					break;
			}
		}

		var validLayers = new Dictionary<string, ResolvedLayer>(StringComparer.Ordinal);
		foreach (var (id, layer) in rawLayers)
		{
			if (rawScenes.TryGetValue(layer.ParentSceneId, out var parentScene))
			{
				validLayers[id] = new ResolvedLayer(id, layer, layer.ParentSceneId, parentScene.Name);
			}
		}

		var effectsByLayerId = new Dictionary<string, List<MeldEffect>>(StringComparer.Ordinal);
		foreach (var (id, effect) in rawEffects)
		{
			if (!validLayers.TryGetValue(effect.ParentLayerId, out var parentLayer))
			{
				continue;
			}

			var list = effectsByLayerId.TryGetValue(effect.ParentLayerId, out var existing)
				? existing
				: effectsByLayerId[effect.ParentLayerId] = [];
			list.Add(new MeldEffect(id,
				effect.Name,
				effect.ParentLayerId,
				parentLayer.Layer.Name,
				parentLayer.SceneId,
				effect.Enabled));
		}

		var layersBySceneId = new Dictionary<string, List<MeldLayer>>(StringComparer.Ordinal);
		foreach (var (id, resolved) in validLayers)
		{
			var effects = effectsByLayerId.TryGetValue(id, out var list)
				? (IReadOnlyList<MeldEffect>)list
				: [];
			var meldLayer = new MeldLayer(id,
				resolved.Layer.Name,
				resolved.Layer.Index,
				resolved.SceneId,
				resolved.SceneName,
				resolved.Layer.Visible,
				effects);

			var sceneLayers = layersBySceneId.TryGetValue(resolved.SceneId, out var existing)
				? existing
				: layersBySceneId[resolved.SceneId] = [];
			sceneLayers.Add(meldLayer);
		}

		var scenes = new List<MeldScene>(rawScenes.Count);
		string? currentSceneId = null;
		string? stagedSceneId = null;

		foreach (var (id, scene) in rawScenes)
		{
			var layers = layersBySceneId.TryGetValue(id, out var sceneLayers)
				? sceneLayers.OrderBy(layer => layer.Index).ThenBy(layer => layer.Name, StringComparer.Ordinal).ToList()
				: [];

			scenes.Add(new MeldScene(id, scene.Name, scene.Index, scene.Current, scene.Staged, layers));

			if (scene.Current)
			{
				currentSceneId = id;
			}

			if (scene.Staged)
			{
				stagedSceneId = id;
			}
		}

		scenes = scenes.OrderBy(scene => scene.Index).ThenBy(scene => scene.Name, StringComparer.Ordinal).ToList();

		var tracks = new List<MeldTrack>(rawTracks.Count);
		foreach (var (id, track) in rawTracks)
		{
			// A track naming a layer that is not (or no longer) in the session degrades to a global
			// track rather than being dropped - it is still a real, controllable track, it just cannot
			// be labelled with a scene.
			string? parentLayerId = null;
			string? sceneName = null;
			if (track.ParentLayerId is { } layerId && validLayers.TryGetValue(layerId, out var parentLayer))
			{
				parentLayerId = layerId;
				sceneName = parentLayer.SceneName;
			}

			tracks.Add(new MeldTrack(id, track.Name, parentLayerId, sceneName, track.Muted, track.Monitoring));
		}

		var scenesById = scenes.ToDictionary(scene => scene.Id, StringComparer.Ordinal);
		var layersById = scenes.SelectMany(scene => scene.Layers)
			.ToDictionary(layer => layer.Id, StringComparer.Ordinal);
		var effectsById = layersById.Values.SelectMany(layer => layer.Effects)
			.ToDictionary(effect => effect.Id, StringComparer.Ordinal);
		var tracksById = tracks.ToDictionary(track => track.Id, StringComparer.Ordinal);

		var hash = 0;
		foreach (var scene in scenes)
		{
			hash ^= HashIdName(scene.Id, scene.Name);
		}

		foreach (var layer in layersById.Values)
		{
			hash ^= HashIdNameParent(layer.Id, layer.Name, layer.SceneId);
		}

		foreach (var effect in effectsById.Values)
		{
			hash ^= HashIdNameParent(effect.Id, effect.Name, effect.LayerId);
		}

		foreach (var track in tracks)
		{
			hash ^= HashIdNameParent(track.Id, track.Name, track.ParentLayerId ?? string.Empty);
		}

		return new MeldSession
		{
			Scenes = scenes,
			Tracks = tracks,
			ScenesById = scenesById,
			LayersById = layersById,
			EffectsById = effectsById,
			TracksById = tracksById,
			CurrentSceneId = currentSceneId,
			StagedSceneId = stagedSceneId,
			StructuralHash = hash
		};
	}

	private static bool TryParseScene(JsonElement value, out RawScene scene)
	{
		scene = default;
		if (ReadString(value, "name") is not { } name ||
			!TryReadInt(value, "index", out var index) ||
			!TryReadBool(value, "current", out var current) ||
			!TryReadBool(value, "staged", out var staged))
		{
			return false;
		}

		scene = new RawScene(name, index, current, staged);
		return true;
	}

	private static bool TryParseLayer(JsonElement value, out RawLayer layer)
	{
		layer = default;
		if (ReadString(value, "parent") is not { } parentSceneId ||
			ReadString(value, "name") is not { } name ||
			!TryReadInt(value, "index", out var index) ||
			!TryReadBool(value, "visible", out var visible))
		{
			return false;
		}

		layer = new RawLayer(parentSceneId, index, name, visible);
		return true;
	}

	private static bool TryParseEffect(JsonElement value, out RawEffect effect)
	{
		effect = default;
		if (ReadString(value, "parent") is not { } parentLayerId ||
			ReadString(value, "name") is not { } name ||
			!TryReadBool(value, "enabled", out var enabled))
		{
			return false;
		}

		effect = new RawEffect(parentLayerId, name, enabled);
		return true;
	}

	private static bool TryParseTrack(JsonElement value, out RawTrack track)
	{
		track = default;
		if (ReadString(value, "name") is not { } name ||
			!TryReadBool(value, "muted", out var muted) ||
			!TryReadBool(value, "monitoring", out var monitoring))
		{
			return false;
		}

		track = new RawTrack(ReadString(value, "parent"), name, muted, monitoring);
		return true;
	}

	private static string? ReadString(JsonElement element, string property)
		=> element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static bool TryReadInt(JsonElement element, string property, out int value)
	{
		value = 0;
		return element.TryGetProperty(property, out var candidate) &&
			candidate.ValueKind == JsonValueKind.Number &&
			candidate.TryGetInt32(out value);
	}

	private static bool TryReadBool(JsonElement element, string property, out bool value)
	{
		value = false;
		if (!element.TryGetProperty(property, out var candidate))
		{
			return false;
		}

		if (candidate.ValueKind == JsonValueKind.True)
		{
			value = true;
			return true;
		}

		return candidate.ValueKind == JsonValueKind.False;
	}

	private static int HashIdName(string id, string name) => HashCode.Combine(id, name);

	private static int HashIdNameParent(string id, string name, string parentId) =>
		HashCode.Combine(id, name, parentId);

	private readonly record struct RawScene(string Name, int Index, bool Current, bool Staged);

	private readonly record struct RawLayer(string ParentSceneId, int Index, string Name, bool Visible);

	private readonly record struct RawEffect(string ParentLayerId, string Name, bool Enabled);

	private readonly record struct RawTrack(string? ParentLayerId, string Name, bool Muted, bool Monitoring);

	private readonly record struct ResolvedLayer(string Id, RawLayer Layer, string SceneId, string SceneName);
}
