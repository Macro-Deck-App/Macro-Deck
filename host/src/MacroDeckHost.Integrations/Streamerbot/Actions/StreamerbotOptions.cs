using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Integrations.Streamerbot.Actions;

internal static class StreamerbotOptions
{
	public static DynamicOptionsResult Actions(StreamerbotConnection? connection)
	{
		var actions = connection?.Catalog.Actions ?? [];

		return new DynamicOptionsResult
		{
			Options = actions
				.OrderBy(action => action.Group ?? string.Empty, StringComparer.OrdinalIgnoreCase)
				.ThenBy(action => action.Name, StringComparer.OrdinalIgnoreCase)
				.Select(action => new ActionParameterOption { Value = action.Id, Label = Label(action) })
				.ToList(),
			AllowsCustomValue = true,
			CacheSeconds = 5
		};
	}

	public static DynamicOptionsResult CodeTriggers(StreamerbotConnection? connection)
	{
		var triggers = connection?.Catalog.CodeTriggers ?? [];

		return new DynamicOptionsResult
		{
			Options = triggers
				.OrderBy(trigger => trigger.Category ?? string.Empty, StringComparer.OrdinalIgnoreCase)
				.ThenBy(trigger => trigger.Name, StringComparer.OrdinalIgnoreCase)
				.Select(trigger => new ActionParameterOption
				{
					Value = trigger.Name,
					Label = string.IsNullOrEmpty(trigger.Category)
						? trigger.Name
						: $"{trigger.Category} / {trigger.Name}"
				})
				.ToList(),
			AllowsCustomValue = true,
			CacheSeconds = 5
		};
	}

	private static string Label(Protocol.StreamerbotActionInfo action)
	{
		var label = string.IsNullOrEmpty(action.Group) ? action.Name : $"{action.Group} / {action.Name}";
		return action.Enabled ? label : $"{label} (disabled)";
	}
}
