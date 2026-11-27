using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Clipboard.Native;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Clipboard;

namespace MacroDeckHost.Integrations.Clipboard.Actions;

internal sealed class ClearActionDefinition : ClipboardActionDefinition
{
	public ClearActionDefinition(ClipboardActionServices services)
		: base(services)
	{
	}

	public override string Id => "clear";

	public override LocalizedText Name => Strings.Actions.Clear.Name();

	public override LocalizedText Description => Strings.Actions.Clear.Description();

	public override ActionRiskCategory RiskCategory => ActionRiskCategory.Input;

	public override IReadOnlyList<ActionParameter> Parameters { get; } = [];

	protected override async Task<ActionResult> RunAsync(ActionExecutionContext context)
	{
		await Services.Monitor.WriteAsync(Services.Backend.ClearAsync,
				ClipboardContentKinds.None,
				null,
				context.CancellationToken)
			.ConfigureAwait(false);
		return ActionResult.Success();
	}
}
