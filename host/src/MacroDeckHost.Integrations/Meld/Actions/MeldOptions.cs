using MacroDeckHost.Integrations.Meld.State;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Meld.Actions;

internal static class MeldOptions
{
	public static IReadOnlyList<ActionParameterOption> Scenes(MeldSession session)
	{
		return session.Scenes.Select(scene => new ActionParameterOption { Value = scene.Id, Label = scene.Name })
			.ToList();
	}

	public static IReadOnlyList<ActionParameterOption> Layers(MeldSession session)
	{
		return session.LayersById.Values
			.Select(layer => new ActionParameterOption
			{
				Value = layer.Id,
				Label = AppStrings.Integrations.Meld.Options.NameInScene(layer.Name, layer.SceneName)
			})
			.ToList();
	}

	public static IReadOnlyList<ActionParameterOption> Effects(MeldSession session)
	{
		return session.EffectsById.Values
			.Select(effect => new ActionParameterOption
			{
				Value = effect.Id,
				Label = AppStrings.Integrations.Meld.Options.NameOnLayer(effect.Name,
					effect.LayerName)
			})
			.ToList();
	}

	public static IReadOnlyList<ActionParameterOption> Tracks(MeldSession session)
	{
		return session.Tracks
			.Select(track => new ActionParameterOption
			{
				Value = track.Id,
				Label = track.ParentLayerId is null
					? track.Name
					: AppStrings.Integrations.Meld.Options.NameInScene(track.Name, track.SceneName!)
			})
			.ToList();
	}
}
