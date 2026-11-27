using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Clipboard.Native;
using MacroDeckHost.Integrations.Clipboard.State;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Clipboard;

namespace MacroDeckHost.Integrations.Clipboard.Actions;

internal sealed class CheckContentActionDefinition : ClipboardActionDefinition
{
	public const string KindParameter = "kind";
	public const string AnyKind = "any";

	public CheckContentActionDefinition(ClipboardActionServices services)
		: base(services)
	{
	}

	public override string Id => "check-content";

	public override LocalizedText Name => Strings.Actions.CheckContent.Name();

	public override LocalizedText Description => Strings.Actions.CheckContent.Description();

	public override ActionRiskCategory RiskCategory => ActionRiskCategory.Input;

	public override IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(KindParameter,
			[
				new ActionParameterOption
				{
					Value = ClipboardContentType.Text, Label = Strings.ContentTypes.Text()
				},
				new ActionParameterOption
				{
					Value = ClipboardContentType.Image, Label = Strings.ContentTypes.Image()
				},
				new ActionParameterOption
				{
					Value = ClipboardContentType.Files, Label = Strings.ContentTypes.Files()
				},
				new ActionParameterOption { Value = AnyKind, Label = Strings.Actions.CheckContent.AnyOption() }
			],
			Strings.Actions.CheckContent.KindLabel(),
			defaultValue: ClipboardContentType.Text,
			required: true),
		ActionParameter.Autocomplete(OutputVariableParameter,
			Strings.Actions.OutputVariableLabel(),
			Strings.Actions.CheckContent.OutputVariableDescription(),
			optionsSourceId: HostOptionsSources.UserVariables,
			required: true)
	];

	internal static bool? Holds(string? kind, ClipboardContentKinds kinds)
	{
		return kind switch
		{
			ClipboardContentType.Text => kinds.HasFlag(ClipboardContentKinds.Text),
			ClipboardContentType.Image => kinds.HasFlag(ClipboardContentKinds.Image),
			ClipboardContentType.Files => kinds.HasFlag(ClipboardContentKinds.Files),
			AnyKind => kinds != ClipboardContentKinds.None,
			_ => null
		};
	}

	protected override async Task<ActionResult> RunAsync(ActionExecutionContext context)
	{
		var variable = ClipboardActionValues.ReadString(context.Parameters, OutputVariableParameter)?.Trim();
		if (string.IsNullOrEmpty(variable))
		{
			return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Errors.NoVariable());
		}

		var kind = ClipboardActionValues.ReadString(context.Parameters, KindParameter) ?? ClipboardContentType.Text;
		var kinds = await Services.Backend.ReadKindsAsync(context.CancellationToken).ConfigureAwait(false);
		if (Holds(kind, kinds) is not { } holds)
		{
			return ActionResult.Failed(ActionErrorCodes.InvalidParameter, Strings.Errors.UnknownContentKind());
		}

		return await WriteVariableAsync(context, variable, VariableType.Boolean, holds).ConfigureAwait(false);
	}
}
