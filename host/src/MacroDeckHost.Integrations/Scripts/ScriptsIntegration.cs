using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Scripts;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Scripts;

[MacroDeckIntegration]
public sealed class ScriptsIntegration : IIntegration, ISystemIntegration
{
	public const string IntegrationId = "app.macro-deck.scripts";

	private IScriptApi? _scripts;

	public ScriptsIntegration()
	{
		Actions = [new RunScriptActionDefinition(() => _scripts)];
	}

	public string Id => IntegrationId;
	public LocalizedText Name => AppStrings.Integrations.Scripts.Name();
	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public bool IsActive => true;
	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context)
	{
		_scripts = context.Scripts;
		return Task.CompletedTask;
	}

	public Task ShutdownAsync() => Task.CompletedTask;
}
