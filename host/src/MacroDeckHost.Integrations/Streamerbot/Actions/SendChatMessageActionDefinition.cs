using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Localization;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Streamerbot.Actions;

internal sealed class SendChatMessageActionDefinition : IActionDefinition
{
	internal const string PlatformParameterName = "platform";
	internal const string MessageParameterName = "message";
	internal const string BotParameterName = "bot";

	private readonly Func<StreamerbotConnection?> _resolver;

	public SendChatMessageActionDefinition(Func<StreamerbotConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "send-chat-message";

	public LocalizedText Name => AppStrings.Integrations.Streamerbot.Actions.SendChatMessageName();

	public LocalizedText Description => AppStrings.Integrations.Streamerbot.Actions.SendChatMessageDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(PlatformParameterName,
			[
				new ActionParameterOption { Value = "twitch", Label = "Twitch" },
				new ActionParameterOption { Value = "youtube", Label = "YouTube" },
				new ActionParameterOption { Value = "trovo", Label = "Trovo" },
				new ActionParameterOption { Value = "kick", Label = "Kick" }
			],
			label: AppStrings.Integrations.Streamerbot.Actions.PlatformLabel(),
			defaultValue: "twitch",
			required: true),
		ActionParameter.MultilineText(MessageParameterName,
			label: AppStrings.Integrations.Streamerbot.Actions.MessageLabel(),
			description: AppStrings.Integrations.Streamerbot.Actions.MessageDescription(),
			required: true,
			maxLength: 500),
		ActionParameter.Toggle(BotParameterName,
			label: AppStrings.Integrations.Streamerbot.Actions.SendAsBotLabel(),
			description: AppStrings.Integrations.Streamerbot.Actions.SendAsBotDescription())
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SendChatMessageActionDefinition>(StreamerbotIntegration.IntegrationId);

		private readonly Func<StreamerbotConnection?> _resolver;

		public Executor(Func<StreamerbotConnection?> resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			if (connection is null)
			{
				_logger.Warning("Streamer.bot chat message skipped: not configured");
				return ActionResult.Failed(ActionErrorCodes.NotConnected,
					AppStrings.Integrations.Streamerbot.Errors.NotConnected());
			}

			if (StreamerbotActionValues.ReadText(context.Parameters, MessageParameterName) is not { } message)
			{
				_logger.Warning("Streamer.bot chat message skipped: the message is empty");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Streamerbot.Errors.NoMessageConfigured());
			}

			var platform = StreamerbotActionValues.ReadText(context.Parameters, PlatformParameterName) ?? "twitch";
			var asBot = StreamerbotActionValues.ReadBool(context.Parameters, BotParameterName);

			await connection.SendChatMessageAsync(platform, message, asBot, context.CancellationToken);

			return ActionResult.Success();
		}
	}
}
