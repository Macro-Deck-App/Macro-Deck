using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Clipboard.Native;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Clipboard;

namespace MacroDeckHost.Integrations.Clipboard.Actions;

internal sealed class SetTextActionDefinition : ClipboardActionDefinition
{
	public const string TextParameter = "text";

	public SetTextActionDefinition(ClipboardActionServices services)
		: base(services)
	{
	}

	public override string Id => "set-text";

	public override LocalizedText Name => Strings.Actions.SetText.Name();

	public override LocalizedText Description => Strings.Actions.SetText.Description();

	public override ActionRiskCategory RiskCategory => ActionRiskCategory.Input;

	public override IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.MultilineText(TextParameter,
			Strings.Actions.SetText.TextLabel(),
			Strings.Actions.SetText.TextDescription("{{ vars.name }}"),
			required: true)
	];

	protected override async Task<ActionResult> RunAsync(ActionExecutionContext context)
	{
		var text = ClipboardActionValues.ReadString(context.Parameters, TextParameter);
		if (string.IsNullOrEmpty(text))
		{
			return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Errors.EnterText());
		}

		await Services.Monitor.WriteAsync(token => Services.Backend.SetTextAsync(text, token),
				ClipboardContentKinds.Text,
				text,
				context.CancellationToken)
			.ConfigureAwait(false);
		return ActionResult.Success();
	}
}
