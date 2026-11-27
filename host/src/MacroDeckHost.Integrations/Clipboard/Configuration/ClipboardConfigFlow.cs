using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Localization;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Clipboard.Config;

namespace MacroDeckHost.Integrations.Clipboard.Configuration;

internal sealed class ClipboardConfigFlow : IConfigFlow
{
	private const string SettingsStepId = "settings";

	private readonly Func<CancellationToken, Task<ClipboardSettings>> _load;

	public ClipboardConfigFlow(Func<CancellationToken, Task<ClipboardSettings>> load)
	{
		_load = load;
	}

	public async Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
	{
		return ConfigFlowResult.Step(await StepAsync(context, cancellationToken).ConfigureAwait(false));
	}

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		if (stepId != SettingsStepId)
		{
			return ConfigFlowResult.Error(await StepAsync(context, cancellationToken).ConfigureAwait(false),
				Strings.UnknownStep());
		}

		var entryTitle = (context as IConfigFlowEntryContext)?.EntryTitle;
		var name = entryTitle ?? (input.GetValueOrDefault(ClipboardConfigKeys.Name) as string ?? string.Empty).Trim();
		if (string.IsNullOrWhiteSpace(name))
		{
			return ConfigFlowResult.Error(await StepAsync(context, cancellationToken).ConfigureAwait(false),
				fieldErrors: new Dictionary<string, LocalizedText>(StringComparer.Ordinal)
				{
					[ClipboardConfigKeys.Name] = Strings.EnterName()
				});
		}

		var watch = ClipboardSettings.ReadFlag(input.GetValueOrDefault(ClipboardConfigKeys.Watch));
		var exposeText = watch && ClipboardSettings.ReadFlag(input.GetValueOrDefault(ClipboardConfigKeys.ExposeText));

		return ConfigFlowResult.Complete(name,
			new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
			{
				[ClipboardConfigKeys.Watch] = ConfigFlowValue.Plain(ClipboardSettings.Format(watch)),
				[ClipboardConfigKeys.ExposeText] = ConfigFlowValue.Plain(ClipboardSettings.Format(exposeText))
			});
	}

	private async Task<ConfigFlowStep> StepAsync(IConfigFlowContext context, CancellationToken cancellationToken)
	{
		var settings = await _load(cancellationToken).ConfigureAwait(false);
		var fields = new List<ActionParameter>();
		if ((context as IConfigFlowEntryContext)?.EntryTitle is null)
		{
			fields.Add(ActionParameter.Text(ClipboardConfigKeys.Name,
				AppStrings.Integrations.Detail.ConfigurationNameLabel(),
				placeholder: Strings.NamePlaceholder(),
				required: true));
		}

		fields.Add(ActionParameter.Toggle(ClipboardConfigKeys.Watch,
			Strings.WatchLabel(),
			Strings.WatchDescription(),
			settings.Watch));
		fields.Add(ActionParameter.Toggle(ClipboardConfigKeys.ExposeText,
				Strings.ExposeTextLabel(),
				Strings.ExposeTextDescription(),
				settings.ShowsText)
			.OnlyWhen(ClipboardConfigKeys.Watch, "true"));

		return new ConfigFlowStep
		{
			StepId = SettingsStepId,
			Title = Strings.Title(),
			Description = Strings.Description(),
			Fields = fields
		};
	}
}
