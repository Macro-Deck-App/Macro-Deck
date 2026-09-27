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

	// Built-in integrations are created by Activator.CreateInstance and never see the DI container, so
	// the host installs its variable lookup here once it is built.
	internal static VariableReader? HostVariableReader { get; set; }

	public VariablesIntegration()
		: this(() => HostVariableReader)
	{
	}

	internal VariablesIntegration(Func<VariableReader?> reader)
	{
		Actions = [new SetVariableActionDefinition(() => _variables), new WriteVariableToFileActionDefinition(reader)];
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
