using MacroDeck.Sdk.Actions;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Obs.Actions;

internal static class ObsSceneItemOptions
{
	public static async Task<IReadOnlyList<ActionParameterOption>> ScenesAndGroupsAsync(ObsConnection connection)
	{
		var scenes = await connection.GetSceneNamesAsync();
		var groups = await connection.GetGroupNamesAsync();

		return scenes.Select(scene => new ActionParameterOption { Value = scene, Label = scene })
			.Concat(groups.Select(group => new ActionParameterOption
			{
				Value = group,
				Label = AppStrings.Integrations.Obs.Params.GroupOption(name: group)
			}))
			.ToList();
	}

	public static async Task<IReadOnlyList<ActionParameterOption>> ItemsAsync(ObsConnection connection, string scene)
	{
		var groups = await connection.GetGroupNamesAsync();
		var items = groups.Contains(scene, StringComparer.Ordinal)
			? await connection.GetGroupItemNamesAsync(scene)
			: await connection.GetSceneItemNamesAsync(scene);

		return items.Select(item => new ActionParameterOption { Value = item, Label = item }).ToList();
	}
}
