using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.DiscordWebhooks.Config;

namespace MacroDeckHost.Integrations.DiscordWebhooks.Configuration;

internal sealed class DiscordWebhooksConfigFlow : IConfigFlow
{
	private const string WebhookStepId = "webhook";

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
	{
		return Task.FromResult(ConfigFlowResult.Step(WebhookStep()));
	}

	public Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		if (stepId != WebhookStepId)
		{
			return Task.FromResult(ConfigFlowResult.Error(WebhookStep(), Strings.UnknownStep()));
		}

		var name = (input.GetValueOrDefault(DiscordWebhooksConfigKeys.Name) as string ?? string.Empty).Trim();
		var url = (input.GetValueOrDefault(DiscordWebhooksConfigKeys.Url) as string ?? string.Empty).Trim();

		var fieldErrors = new Dictionary<string, LocalizedText>(StringComparer.Ordinal);
		if (name.Length == 0)
		{
			fieldErrors[DiscordWebhooksConfigKeys.Name] = Strings.NameRequired();
		}

		if (!DiscordWebhookUrl.TryParse(url, out var uri))
		{
			fieldErrors[DiscordWebhooksConfigKeys.Url] = Strings.UrlInvalid();
		}

		if (fieldErrors.Count > 0)
		{
			return Task.FromResult(ConfigFlowResult.Error(WebhookStep(), fieldErrors: fieldErrors));
		}

		var values = new Dictionary<string, ConfigFlowValue>(StringComparer.Ordinal)
		{
			[DiscordWebhooksConfigKeys.Name] = ConfigFlowValue.Plain(name),
			[DiscordWebhooksConfigKeys.Url] = ConfigFlowValue.Secret(uri.AbsoluteUri)
		};

		return Task.FromResult(ConfigFlowResult.Complete(name, values));
	}

	private static ConfigFlowStep WebhookStep()
	{
		return new ConfigFlowStep
		{
			StepId = WebhookStepId,
			Title = Strings.Title(),
			Description = Strings.Description(),
			Instructions =
			[
				new ConfigFlowInstruction { Text = Strings.InstructionCreate() },
				new ConfigFlowInstruction { Text = Strings.InstructionCopy() }
			],
			Fields =
			[
				ActionParameter.Text(DiscordWebhooksConfigKeys.Name,
					Strings.NameLabel(),
					Strings.NameDescription(),
					required: true),
				ActionParameter.Secret(DiscordWebhooksConfigKeys.Url,
					Strings.UrlLabel(),
					Strings.UrlDescription(),
					true)
			]
		};
	}
}
