namespace MacroDeck.Sdk.Actions;

public interface IDynamicOptionsActionDefinition : IActionDefinition
{
	Task<DynamicOptionsResult> GetDynamicOptionsAsync(DynamicOptionsContext context,
		CancellationToken cancellationToken);
}
