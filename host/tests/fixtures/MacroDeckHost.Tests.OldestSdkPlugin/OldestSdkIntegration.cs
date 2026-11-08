using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.OldestSdkPlugin;

public sealed class OldestSdkIntegration : IPluginIntegration, IVariableProvider
{
	public const string RememberActionId = "remember-text";

	public const string TextParameterName = "text";

	public const string RememberedVariableName = "oldest_sdk_remembered_text";

	private const string RememberedVariableId = "remembered-text";

	private volatile string _remembered = "";

	public OldestSdkIntegration()
	{
		Actions = [new RememberTextAction(this)];
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public IReadOnlyList<VariableDefinition> Variables { get; } =
	[
		VariableDefinition.Eager(RememberedVariableName, VariableType.Text) with { Id = RememberedVariableId }
	];

	public Task InitializeAsync(IIntegrationContext context)
	{
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		return Task.CompletedTask;
	}

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{
		return ValueTask.FromResult(string.Equals(localId, RememberedVariableId, StringComparison.Ordinal)
			? VariableReading.Of(_remembered)
			: VariableReading.Unavailable);
	}

	private sealed class RememberTextAction(OldestSdkIntegration integration) : IActionDefinition
	{
		public string Id => RememberActionId;

		public LocalizedText Name => "Remember text";

		public LocalizedText Description => "Stores the text so the remembered-text variable reports it.";

		public IReadOnlyList<ActionParameter> Parameters { get; } =
			[ActionParameter.Text(TextParameterName, "Text", required: true)];

		public IActionExecutor CreateExecutor()
		{
			return new Executor(integration);
		}
	}

	private sealed class Executor(OldestSdkIntegration integration) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (context.Parameters.GetValueOrDefault(TextParameterName) is not string text)
			{
				return Task.FromResult(ActionResult.Failed(ActionErrorCodes.InvalidParameter, "text is required."));
			}

			integration._remembered = text;
			return ActionResult.SucceededTask;
		}
	}
}
