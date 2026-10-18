using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Integrations.DiscordWebhooks.Models;
using MacroDeckHost.Integrations.DiscordWebhooks.Protocol;
using MacroDeckHost.Integrations.Ui;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.DiscordWebhooks;

namespace MacroDeckHost.Integrations.DiscordWebhooks.Actions;

internal sealed class SendMessageActionDefinition : IUiConfigurableActionDefinition, IDynamicOptionsActionDefinition
{
	public const string WebhookParameter = "webhook";
	public const string WebhookUrlParameter = "webhookUrl";
	public const string MessageParameter = "message";

	private readonly DiscordWebhookTargets _targets;
	private readonly IDiscordWebhookSender _sender;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	public SendMessageActionDefinition(
		DiscordWebhookTargets targets,
		IDiscordWebhookSender sender,
		TimeProvider time,
		ILogger logger)
	{
		_targets = targets;
		_sender = sender;
		_time = time;
		_logger = logger;
	}

	public string Id => "send-message";

	public LocalizedText Name => Strings.Actions.SendMessageName();

	public LocalizedText Description => Strings.Actions.SendMessageDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice(WebhookParameter,
			Strings.Actions.WebhookLabel(),
			Strings.Actions.WebhookDescription(),
			placeholder: Strings.Actions.WebhookPlaceholder()),
		ActionParameter.Secret(WebhookUrlParameter,
			Strings.Actions.WebhookUrlLabel(),
			Strings.Actions.WebhookUrlDescription()),
		ActionParameter.KeyValue(MessageParameter,
			Strings.Actions.MessageLabel(),
			Strings.Actions.MessageDescription())
	];

	public IActionExecutor CreateExecutor()
	{
		return new Executor(_targets, _sender, _time, _logger);
	}

	public async Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (context.ParameterName != WebhookParameter)
		{
			return new DynamicOptionsResult { Options = [] };
		}

		var entries = await _targets.GetEntriesAsync(cancellationToken).ConfigureAwait(false);

		return new DynamicOptionsResult
		{
			Options =
			[
				.. entries.Select(entry
					=> new ActionParameterOption { Value = entry.Id.ToString(), Label = entry.Title }),
				new ActionParameterOption
				{
					Value = DiscordWebhookTargets.Custom, Label = Strings.Actions.CustomWebhook()
				}
			],
			CacheSeconds = 5
		};
	}

	public async Task<IUiSession?> CreateConfigurationSessionAsync(
		ActionConfigurationRequest request,
		CancellationToken cancellationToken)
	{
		var entries = await _targets.GetEntriesAsync(cancellationToken).ConfigureAwait(false);
		var editor = new DiscordMessageEditor(request.Parameters, entries, _targets, _sender, _time, _logger);
		var session = new ActionConfigurationSession(new UiViewSession(new UiView(request.Session.Surface,
			editor.Build())));
		editor.Report = session.Report;

		return session;
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly DiscordWebhookTargets _targets;
		private readonly IDiscordWebhookSender _sender;
		private readonly TimeProvider _time;
		private readonly ILogger _logger;

		public Executor(DiscordWebhookTargets targets, IDiscordWebhookSender sender, TimeProvider time, ILogger logger)
		{
			_targets = targets;
			_sender = sender;
			_time = time;
			_logger = logger;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var webhook = context.Parameters.GetValueOrDefault(WebhookParameter) as string;
			var inlineUrl = context.Parameters.GetValueOrDefault(WebhookUrlParameter) as string;

			var target = await _targets.ResolveAsync(webhook, inlineUrl, context.CancellationToken)
				.ConfigureAwait(false);
			if (target is not { Kind: DiscordWebhookTargetKind.Resolved, Url: { } url })
			{
				_logger.Warning("Discord webhook message was not sent to {Webhook}: {Reason}",
					target.LogName,
					target.Kind);
				var (code, message) = DiscordWebhookErrors.Describe(target.Kind);
				return ActionResult.Failed(code, message);
			}

			var draft = DiscordMessageMap.Parse(
				DiscordMessageMap.Read(context.Parameters.GetValueOrDefault(MessageParameter)));
			var violations = DiscordMessageValidator.Validate(draft, false);
			if (violations.Count > 0)
			{
				_logger.Warning("Discord webhook message to {Webhook} was not sent: {Count} validation problems",
					target.LogName,
					violations.Count);
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					Strings.Errors.InvalidMessage(violations[0].Message));
			}

			var result = await _sender
				.SendAsync(url, DiscordPayloadBuilder.Build(draft, _time.GetUtcNow()), context.CancellationToken)
				.ConfigureAwait(false);

			if (result.Outcome == DiscordSendOutcome.Sent)
			{
				return ActionResult.Success();
			}

			_logger.Warning("Discord webhook {Webhook} did not accept the message: {Outcome} ({Status}) {Reason}",
				target.LogName,
				result.Outcome,
				result.Status,
				result.Reason);
			var (failureCode, failure) = DiscordWebhookErrors.Describe(result);
			return ActionResult.Failed(failureCode, failure);
		}
	}
}
