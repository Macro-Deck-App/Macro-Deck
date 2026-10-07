using MacroDeck.Sdk.Scripts;

namespace MacroDeckHost.Plugins.Capabilities.Callbacks;

// An SDK older than the Color input type throws on "color" while reading the scripts push and is left
// with no scripts at all, so a session that did not negotiate the feature gets Color inputs as text.
public static class ScriptWireCompatibility
{
	public static IReadOnlyList<Script> ToWirePayload(IReadOnlyList<Script> scripts, bool supportsColorInputs)
		=> supportsColorInputs || !scripts.Any(script => script.Inputs.Any(input => input.Type == ScriptInputType.Color))
			? scripts
			: [.. scripts.Select(WithoutColorInputs)];

	private static Script WithoutColorInputs(Script script)
		=> new()
		{
			Id = script.Id,
			Name = script.Name,
			Description = script.Description,
			RunsOnWidget = script.RunsOnWidget,
			Inputs =
			[
				.. script.Inputs.Select(input => input.Type == ScriptInputType.Color
					? new ScriptInput
					{
						Name = input.Name,
						Type = ScriptInputType.Text,
						Label = input.Label,
						Description = input.Description,
						Required = input.Required,
						DefaultValue = input.DefaultValue
					}
					: input)
			]
		};
}
