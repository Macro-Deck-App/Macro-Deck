using System.Globalization;
using MacroDeckHost.Integrations.Discord.Rpc;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.Discord.Actions;

internal sealed class SetRichPresenceActionDefinition : IActionDefinition
{
	internal const string TypeParameter = "activityType";
	internal const string DetailsParameter = "details";
	internal const string StateParameter = "state";
	internal const string LargeImageParameter = "largeImage";
	internal const string LargeTextParameter = "largeText";
	internal const string SmallImageParameter = "smallImage";
	internal const string SmallTextParameter = "smallText";
	internal const string ElapsedParameter = "showElapsed";

	private readonly Func<DiscordConnection?> _resolver;

	public SetRichPresenceActionDefinition(Func<DiscordConnection?> resolver)
	{
		_resolver = resolver;
	}

	public string Id => "set-rich-presence";

	public LocalizedText Name => AppStrings.Integrations.Discord.Actions.SetRichPresence.Name();

	public LocalizedText Description => AppStrings.Integrations.Discord.Actions.SetRichPresence.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(TypeParameter,
			options:
			[
				new ActionParameterOption
					{ Value = "0", Label = AppStrings.Integrations.Discord.Actions.SetRichPresence.PlayingOption() },
				new ActionParameterOption
				{
					Value = "2", Label = AppStrings.Integrations.Discord.Actions.SetRichPresence.ListeningToOption()
				},
				new ActionParameterOption
					{ Value = "3", Label = AppStrings.Integrations.Discord.Actions.SetRichPresence.WatchingOption() },
				new ActionParameterOption
				{
					Value = "5", Label = AppStrings.Integrations.Discord.Actions.SetRichPresence.CompetingInOption()
				}
			],
			label: AppStrings.Integrations.Discord.Actions.SetRichPresence.ActivityTypeLabel(),
			defaultValue: "0"),
		ActionParameter.Text(DetailsParameter,
			label: AppStrings.Integrations.Discord.Actions.SetRichPresence.DetailsLabel(),
			description: AppStrings.Integrations.Discord.Actions.SetRichPresence.DetailsDescription(),
			maxLength: 128),
		ActionParameter.Text(StateParameter,
			label: AppStrings.Integrations.Discord.Actions.SetRichPresence.StateLabel(),
			description: AppStrings.Integrations.Discord.Actions.SetRichPresence.StateDescription(),
			maxLength: 128),
		ActionParameter.Text(LargeImageParameter,
			label: AppStrings.Integrations.Discord.Actions.SetRichPresence.LargeImageLabel(),
			description: AppStrings.Integrations.Discord.Actions.SetRichPresence.LargeImageDescription()),
		ActionParameter.Text(LargeTextParameter,
			label: AppStrings.Integrations.Discord.Actions.SetRichPresence.LargeTextLabel(),
			maxLength: 128),
		ActionParameter.Text(SmallImageParameter,
			label: AppStrings.Integrations.Discord.Actions.SetRichPresence.SmallImageLabel()),
		ActionParameter.Text(SmallTextParameter,
			label: AppStrings.Integrations.Discord.Actions.SetRichPresence.SmallTextLabel(),
			maxLength: 128),
		ActionParameter.Toggle(ElapsedParameter,
			label: AppStrings.Integrations.Discord.Actions.SetRichPresence.ShowElapsedLabel(),
			description: AppStrings.Integrations.Discord.Actions.SetRichPresence.ShowElapsedDescription(),
			defaultValue: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_resolver);

	internal static DiscordActivity? BuildActivity(
		IReadOnlyDictionary<string, object> parameters,
		DateTimeOffset now)
	{
		var details = DiscordActionParameters.ReadText(parameters.GetValueOrDefault(DetailsParameter));
		var state = DiscordActionParameters.ReadText(parameters.GetValueOrDefault(StateParameter));
		var largeImage = DiscordActionParameters.ReadText(parameters.GetValueOrDefault(LargeImageParameter));
		var largeText = DiscordActionParameters.ReadText(parameters.GetValueOrDefault(LargeTextParameter));
		var smallImage = DiscordActionParameters.ReadText(parameters.GetValueOrDefault(SmallImageParameter));
		var smallText = DiscordActionParameters.ReadText(parameters.GetValueOrDefault(SmallTextParameter));

		if (details is null && state is null && largeImage is null && smallImage is null)
		{
			return null;
		}

		var assets = largeImage is null && largeText is null && smallImage is null && smallText is null
			? null
			: new DiscordActivityAssets
			{
				LargeImage = largeImage,
				LargeText = largeText,
				SmallImage = smallImage,
				SmallText = smallText
			};

		var timestamps = DiscordActionParameters.ReadBool(parameters.GetValueOrDefault(ElapsedParameter))
			? new DiscordActivityTimestamps { Start = now.ToUnixTimeMilliseconds() }
			: null;

		return new DiscordActivity
		{
			Type = ReadActivityType(parameters.GetValueOrDefault(TypeParameter)),
			Details = details,
			State = state,
			Assets = assets,
			Timestamps = timestamps
		};
	}

	private static int ReadActivityType(object? raw)
	{
		var parsed = raw switch
		{
			int i => i,
			string s when int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) => value,
			double d => (int)d,
			_ => 0
		};

		return parsed is 0 or 2 or 3 or 5 ? parsed : 0;
	}

	private sealed class Executor : IActionExecutor
	{
		private static readonly ILogger _logger =
			IntegrationLog.For<SetRichPresenceActionDefinition>(DiscordIntegration.IntegrationId);

		private readonly Func<DiscordConnection?> _resolver;

		public Executor(Func<DiscordConnection?> resolver)
		{
			_resolver = resolver;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var connection = _resolver();
			if (connection is null)
			{
				_logger.Warning("Discord rich presence action skipped: Discord is not set up");
				return ActionResult.Failed(ActionErrorCodes.NotConfigured,
					AppStrings.Integrations.Discord.Errors.NotSetUp());
			}

			var activity = BuildActivity(context.Parameters, DateTimeOffset.UtcNow);
			if (activity is null)
			{
				_logger.Warning("Discord rich presence action skipped: nothing to show was configured");
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Discord.Errors.NoRichPresenceDetails());
			}

			await connection.SetActivityAsync(activity, context.CancellationToken).ConfigureAwait(false);

			return ActionResult.Success();
		}
	}
}
