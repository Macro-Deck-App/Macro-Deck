using System.Globalization;
using MacroDeckHost.Integrations.Discord.Webhooks;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Discord.Actions;

internal sealed class ExecuteWebhookActionDefinition : IActionDefinition
{
	internal const string UrlParameter = "webhookUrl";
	internal const string ContentParameter = "content";
	internal const string UsernameParameter = "username";
	internal const string AvatarParameter = "avatarUrl";
	internal const string TtsParameter = "tts";
	internal const string EmbedTitleParameter = "embedTitle";
	internal const string EmbedDescriptionParameter = "embedDescription";
	internal const string EmbedUrlParameter = "embedUrl";
	internal const string EmbedColorParameter = "embedColor";
	internal const string EmbedImageParameter = "embedImageUrl";
	internal const string EmbedThumbnailParameter = "embedThumbnailUrl";
	internal const string EmbedFooterParameter = "embedFooter";

	private readonly IDiscordWebhookClient _client;

	public ExecuteWebhookActionDefinition(IDiscordWebhookClient client)
	{
		_client = client;
	}

	public string Id => "execute-webhook";

	public LocalizedText Name => AppStrings.Integrations.Discord.Actions.ExecuteWebhook.Name();

	public LocalizedText Description => AppStrings.Integrations.Discord.Actions.ExecuteWebhook.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Url(UrlParameter,
			label: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.UrlLabel(),
			description: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.UrlDescription(),
			placeholder: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.UrlPlaceholder(),
			required: true),
		ActionParameter.MultilineText(ContentParameter,
			label: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.MessageLabel(),
			description: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.MessageDescription(),
			maxLength: 2000),
		ActionParameter.Text(UsernameParameter,
			label: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.UsernameLabel(),
			description: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.UsernameDescription()),
		ActionParameter.Url(AvatarParameter,
			label: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.AvatarLabel(),
			description: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.AvatarDescription()),
		ActionParameter.Toggle(TtsParameter, label: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.TtsLabel()),
		ActionParameter.Text(EmbedTitleParameter,
			label: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.EmbedTitleLabel(),
			maxLength: 256),
		ActionParameter.MultilineText(EmbedDescriptionParameter,
			label: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.EmbedTextLabel(),
			maxLength: 4096),
		ActionParameter.Url(EmbedUrlParameter,
			label: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.EmbedLinkLabel(),
			description: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.EmbedLinkDescription()),
		ActionParameter.Color(EmbedColorParameter,
			label: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.EmbedColourLabel()),
		ActionParameter.Url(EmbedImageParameter,
			label: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.EmbedImageLabel()),
		ActionParameter.Url(EmbedThumbnailParameter,
			label: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.EmbedThumbnailLabel()),
		ActionParameter.Text(EmbedFooterParameter,
			label: AppStrings.Integrations.Discord.Actions.ExecuteWebhook.EmbedFooterLabel(),
			maxLength: 2048)
	];

	public IActionExecutor CreateExecutor() => new Executor(_client);

	internal static DiscordWebhookRequest BuildRequest(IReadOnlyDictionary<string, object> parameters)
	{
		var embed = new DiscordWebhookEmbed
		{
			Title = DiscordActionParameters.ReadText(parameters.GetValueOrDefault(EmbedTitleParameter)),
			Description = DiscordActionParameters.ReadText(parameters.GetValueOrDefault(EmbedDescriptionParameter)),
			Url = DiscordActionParameters.ReadText(parameters.GetValueOrDefault(EmbedUrlParameter)),
			Color = ParseColor(DiscordActionParameters.ReadText(parameters.GetValueOrDefault(EmbedColorParameter))),
			Image = ImageOrNull(parameters.GetValueOrDefault(EmbedImageParameter)),
			Thumbnail = ImageOrNull(parameters.GetValueOrDefault(EmbedThumbnailParameter)),
			Footer = FooterOrNull(parameters.GetValueOrDefault(EmbedFooterParameter))
		};

		var tts = DiscordActionParameters.ReadBool(parameters.GetValueOrDefault(TtsParameter));

		return new DiscordWebhookRequest
		{
			Content = DiscordActionParameters.ReadText(parameters.GetValueOrDefault(ContentParameter)),
			Username = DiscordActionParameters.ReadText(parameters.GetValueOrDefault(UsernameParameter)),
			AvatarUrl = DiscordActionParameters.ReadText(parameters.GetValueOrDefault(AvatarParameter)),
			Tts = tts ? true : null,
			Embeds = embed.IsEmpty ? null : [embed]
		};
	}

	internal static int? ParseColor(string? value)
	{
		if (value is null)
		{
			return null;
		}

		var hex = value.TrimStart('#');
		if (hex.Length == 8)
		{
			hex = hex[..6];
		}

		return hex.Length == 6 &&
			int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed)
				? parsed
				: null;
	}

	private static DiscordWebhookEmbedImage? ImageOrNull(object? raw)
	{
		var url = DiscordActionParameters.ReadText(raw);
		return url is null ? null : new DiscordWebhookEmbedImage { Url = url };
	}

	private static DiscordWebhookEmbedFooter? FooterOrNull(object? raw)
	{
		var text = DiscordActionParameters.ReadText(raw);
		return text is null ? null : new DiscordWebhookEmbedFooter { Text = text };
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<ExecuteWebhookActionDefinition>(DiscordIntegration.IntegrationId);

		private readonly IDiscordWebhookClient _client;

		public Executor(IDiscordWebhookClient client)
		{
			_client = client;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var url = DiscordActionParameters.ReadText(context.Parameters.GetValueOrDefault(UrlParameter));
			if (url is null)
			{
				_logger.Warning("Discord webhook action skipped: no webhook URL configured");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Discord.Errors.NoWebhookUrl());
			}

			var failure = await _client
				.ExecuteAsync(url, BuildRequest(context.Parameters), context.CancellationToken)
				.ConfigureAwait(false);

			if (failure is not null)
			{
				_logger.Warning("Discord webhook was not sent: {Reason}", failure);
				return ActionResult.Failed(ActionErrorCodes.ProviderError,
					AppStrings.Integrations.Discord.Errors.WebhookFailed(details: failure));
			}

			return ActionResult.Success();
		}
	}
}
