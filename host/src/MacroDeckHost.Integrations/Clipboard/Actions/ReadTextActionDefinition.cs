using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Clipboard;

namespace MacroDeckHost.Integrations.Clipboard.Actions;

internal sealed class ReadTextActionDefinition : ClipboardActionDefinition
{
	public const int TextLimit = 1024 * 1024;

	public ReadTextActionDefinition(ClipboardActionServices services)
		: base(services)
	{
	}

	public override string Id => "read-text";

	public override LocalizedText Name => Strings.Actions.ReadText.Name();

	public override LocalizedText Description => Strings.Actions.ReadText.Description();

	public override ActionRiskCategory RiskCategory => ActionRiskCategory.Input;

	public override IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete(OutputVariableParameter,
			Strings.Actions.OutputVariableLabel(),
			Strings.Actions.ReadText.OutputVariableDescription(),
			optionsSourceId: HostOptionsSources.UserVariables,
			required: true)
	];

	protected override async Task<ActionResult> RunAsync(ActionExecutionContext context)
	{
		var variable = ClipboardActionValues.ReadString(context.Parameters, OutputVariableParameter)?.Trim();
		if (string.IsNullOrEmpty(variable))
		{
			return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Errors.NoVariable());
		}

		var reading = await Services.Backend.ReadTextAsync(TextLimit, context.CancellationToken)
			.ConfigureAwait(false);
		if (reading.IsTooLarge)
		{
			return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Errors.TextTooLarge());
		}

		if (reading.Text is not { } text)
		{
			return ActionResult.Failed(ActionErrorCodes.NotFound, Strings.Errors.NoText());
		}

		return await WriteVariableAsync(context, variable, VariableType.Text, text).ConfigureAwait(false);
	}
}
