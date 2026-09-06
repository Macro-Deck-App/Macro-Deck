using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Discord.Actions;

internal sealed class SelectChannelActionDefinition : IDynamicOptionsActionDefinition
{
	internal const string GuildParameter = "guildId";
	internal const string ChannelParameter = "channelId";
	internal const string ForceParameter = "force";

	private static readonly int[] _voiceChannelTypes =
		[DiscordChannelTypes.GuildVoice, DiscordChannelTypes.GuildStageVoice];

	private static readonly int[] _textChannelTypes = [DiscordChannelTypes.GuildText];

	private readonly Func<DiscordConnection?> _resolver;
	private readonly bool _voice;

	public SelectChannelActionDefinition(Func<DiscordConnection?> resolver, bool voice)
	{
		_resolver = resolver;
		_voice = voice;

		Parameters = voice
			?
			[
				ActionParameter.DynamicChoice(GuildParameter,
					label: AppStrings.Integrations.Discord.Actions.SelectChannel.ServerLabel(),
					required: true),
				ActionParameter.DynamicChoice(ChannelParameter,
					label: AppStrings.Integrations.Discord.Actions.SelectChannel.VoiceChannelLabel(),
					required: true),
				ActionParameter.Toggle(ForceParameter,
					label: AppStrings.Integrations.Discord.Actions.SelectChannel.ForceLabel(),
					description: AppStrings.Integrations.Discord.Actions.SelectChannel.ForceDescription(),
					defaultValue: true)
			]
			:
			[
				ActionParameter.DynamicChoice(GuildParameter,
					label: AppStrings.Integrations.Discord.Actions.SelectChannel.ServerLabel(),
					required: true),
				ActionParameter.DynamicChoice(ChannelParameter,
					label: AppStrings.Integrations.Discord.Actions.SelectChannel.TextChannelLabel(),
					required: true)
			];
	}

	public string Id => _voice ? "join-voice-channel" : "open-text-channel";

	public LocalizedText Name => _voice
		? AppStrings.Integrations.Discord.Actions.SelectChannel.JoinVoiceName()
		: AppStrings.Integrations.Discord.Actions.SelectChannel.OpenTextName();

	public LocalizedText Description => _voice
		? AppStrings.Integrations.Discord.Actions.SelectChannel.JoinVoiceDescription()
		: AppStrings.Integrations.Discord.Actions.SelectChannel.OpenTextDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor() => new Executor(_resolver, _voice);

	public async Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var connection = _resolver();
		var options = connection is null
			? []
			: await LoadOptionsAsync(connection, context, cancellationToken).ConfigureAwait(false);

		return new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = true,
			CacheSeconds = 15
		};
	}

	private async Task<IReadOnlyList<ActionParameterOption>> LoadOptionsAsync(
		DiscordConnection connection,
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (context.ParameterName == GuildParameter)
		{
			var guilds = await connection.GetGuildsAsync(cancellationToken).ConfigureAwait(false);
			return [.. guilds.Select(g => new ActionParameterOption { Value = g.Id, Label = g.Name })];
		}

		if (context.ParameterName != ChannelParameter)
		{
			return [];
		}

		if (context.CurrentParameters.GetValueOrDefault(GuildParameter) is not string guildId ||
			string.IsNullOrWhiteSpace(guildId))
		{
			return [];
		}

		var types = _voice ? _voiceChannelTypes : _textChannelTypes;
		var channels = await connection.GetChannelsAsync(guildId, types, cancellationToken).ConfigureAwait(false);
		return [.. channels.Select(c => new ActionParameterOption { Value = c.Id, Label = c.Name })];
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SelectChannelActionDefinition>(DiscordIntegration.IntegrationId);

		private readonly Func<DiscordConnection?> _resolver;
		private readonly bool _voice;

		public Executor(Func<DiscordConnection?> resolver, bool voice)
		{
			_resolver = resolver;
			_voice = voice;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			if (connection is null)
			{
				_logger.Warning("Discord channel action skipped: Discord is not set up");
				return ActionResult.Failed(ActionErrorCodes.NotConfigured,
					AppStrings.Integrations.Discord.Errors.NotSetUp());
			}

			if (context.Parameters.GetValueOrDefault(ChannelParameter) is not string channelId ||
				string.IsNullOrWhiteSpace(channelId))
			{
				_logger.Warning("Discord channel action skipped: no channel selected");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Discord.Errors.NoChannelSelected());
			}

			if (_voice)
			{
				var force = DiscordActionParameters.ReadBool(context.Parameters.GetValueOrDefault(ForceParameter));
				await connection
					.JoinVoiceChannelAsync(channelId.Trim(), force, context.CancellationToken)
					.ConfigureAwait(false);
				return ActionResult.Success();
			}

			await connection
				.SelectTextChannelAsync(channelId.Trim(), context.CancellationToken)
				.ConfigureAwait(false);

			return ActionResult.Success();
		}
	}
}
