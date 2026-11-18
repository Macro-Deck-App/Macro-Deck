using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Actions.Options;
using MacroDeckHost.Application.Variables.Templates;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Infrastructure.SystemIntegrations.Variables;

internal delegate Task<TemplateVariableRefreshStatus> TemplateVariableRefresh(
	string name,
	UserVariableContext variableContext,
	CancellationToken cancellationToken);

internal sealed class RefreshTemplateVariableActionDefinition : IActionDefinition
{
	private readonly Func<TemplateVariableRefresh?> _refresh;

	public RefreshTemplateVariableActionDefinition(Func<TemplateVariableRefresh?> refresh)
	{
		_refresh = refresh;
	}

	public string Id => "refresh-template-variable";
	public LocalizedText Name => AppStrings.Integrations.Variables.Actions.RefreshTemplateName();
	public LocalizedText Description => AppStrings.Integrations.Variables.Actions.RefreshTemplateDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete("variable",
			AppStrings.Integrations.Variables.Actions.VariableLabel(),
			AppStrings.Integrations.Variables.Actions.RefreshTemplateVariableDescription(),
			optionsSourceId: VariableOptionsSourceIds.TemplateVariables,
			required: true)
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_refresh);
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<TemplateVariableRefresh?> _refresh;

		public Executor(Func<TemplateVariableRefresh?> refresh)
		{
			_refresh = refresh;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			if (_refresh() is not { } refresh)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.Variables.Errors.VariablesUnavailable());
			}

			var name = Read(context, "variable");
			if (string.IsNullOrWhiteSpace(name))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Variables.Errors.NoVariableSelected());
			}

			var status = await refresh(name, UserVariableContext.From(context), context.CancellationToken);
			return status switch
			{
				TemplateVariableRefreshStatus.Refreshed => ActionResult.Success(),
				TemplateVariableRefreshStatus.NotFound => ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.Variables.Errors.VariableNotFound()),
				TemplateVariableRefreshStatus.NotTemplate => ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Variables.Errors.NotATemplateVariable()),
				_ => ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.Variables.Errors.VariableValueUnavailable())
			};
		}

		private static string? Read(ActionExecutionContext context, string name)
		{
			if (!context.Parameters.TryGetValue(name, out var value))
			{
				return null;
			}

			return value is IFormattable formattable
				? formattable.ToString(null, CultureInfo.InvariantCulture)
				: value.ToString();
		}
	}
}
