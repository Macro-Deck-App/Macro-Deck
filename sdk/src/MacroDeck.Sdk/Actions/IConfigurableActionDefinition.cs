namespace MacroDeck.Sdk.Actions;

public interface IConfigurableActionDefinition : IActionDefinition
{
	string? DescriptiveUiSchema { get; }
}
