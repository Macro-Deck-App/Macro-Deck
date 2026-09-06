using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Variables;

[MacroDeckIntegration]
public sealed class VariablesIntegration : IIntegration, ISystemIntegration
{
	public const string IntegrationId = "app.macro-deck.variables";

	private IUserVariableApi? _variables;

	public VariablesIntegration()
	{
		Actions = [new SetVariableActionDefinition(() => _variables)];
	}

	public string Id => IntegrationId;
	public LocalizedText Name => AppStrings.Integrations.Variables.Name();
	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public bool IsActive => true;
	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context)
	{
		_variables = context.UserVariables;
		return Task.CompletedTask;
	}

	public Task ShutdownAsync() => Task.CompletedTask;
}
