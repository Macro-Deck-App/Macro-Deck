using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal static class MeldOptions
{
	public static IReadOnlyList<ActionParameterOption> Scenes(MeldSession session)
		=> session.Scenes.Select(scene => new ActionParameterOption { Value = scene.Id, Label = scene.Name }).ToList();

	public static IReadOnlyList<ActionParameterOption> Layers(MeldSession session)
		=> session.LayersById.Values
			.Select(layer => new ActionParameterOption
			{
				Value = layer.Id,
				Label = AppStrings.Integrations.Meld.Options.NameInScene(name: layer.Name, sceneName: layer.SceneName)
			})
			.ToList();

	public static IReadOnlyList<ActionParameterOption> Effects(MeldSession session)
		=> session.EffectsById.Values
			.Select(effect => new ActionParameterOption
			{
				Value = effect.Id,
				Label = AppStrings.Integrations.Meld.Options.NameOnLayer(name: effect.Name,
					layerName: effect.LayerName)
			})
			.ToList();

	public static IReadOnlyList<ActionParameterOption> Tracks(MeldSession session)
		=> session.Tracks
			.Select(track => new ActionParameterOption
			{
				Value = track.Id,
				Label = track.ParentLayerId is null
					? track.Name
					: AppStrings.Integrations.Meld.Options.NameInScene(name: track.Name, sceneName: track.SceneName!)
			})
			.ToList();
}
