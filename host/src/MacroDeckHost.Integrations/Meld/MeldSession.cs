namespace MacroDeckHost.Integrations.Meld;

internal sealed record MeldScene(
	string Id,
	string Name,
	int Index,
	bool IsCurrent,
	bool IsStaged,
	IReadOnlyList<MeldLayer> Layers);

internal sealed record MeldLayer(
	string Id,
	string Name,
	int Index,
	string SceneId,
	string SceneName,
	bool Visible,
	IReadOnlyList<MeldEffect> Effects);

internal sealed record MeldEffect(
	string Id,
	string Name,
	string LayerId,
	string LayerName,
	string SceneId,
	bool Enabled);

internal sealed record MeldTrack(
	string Id,
	string Name,
	string? ParentLayerId,
	string? SceneName,
	bool Muted,
	bool Monitoring);

internal sealed record MeldSession
{
	public static MeldSession Empty { get; } = new()
	{
		Scenes = [],
		Tracks = [],
		ScenesById = new Dictionary<string, MeldScene>(StringComparer.Ordinal),
		LayersById = new Dictionary<string, MeldLayer>(StringComparer.Ordinal),
		EffectsById = new Dictionary<string, MeldEffect>(StringComparer.Ordinal),
		TracksById = new Dictionary<string, MeldTrack>(StringComparer.Ordinal),
		CurrentSceneId = null,
		StagedSceneId = null,
		StructuralHash = 0
	};

	public required IReadOnlyList<MeldScene> Scenes { get; init; }

	public required IReadOnlyList<MeldTrack> Tracks { get; init; }

	public required IReadOnlyDictionary<string, MeldScene> ScenesById { get; init; }

	public required IReadOnlyDictionary<string, MeldLayer> LayersById { get; init; }

	public required IReadOnlyDictionary<string, MeldEffect> EffectsById { get; init; }

	public required IReadOnlyDictionary<string, MeldTrack> TracksById { get; init; }

	public required string? CurrentSceneId { get; init; }

	public required string? StagedSceneId { get; init; }

	public required int StructuralHash { get; init; }
}
