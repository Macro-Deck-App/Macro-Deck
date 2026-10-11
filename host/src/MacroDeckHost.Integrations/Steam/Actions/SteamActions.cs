using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Steam;
using ActionStrings = MacroDeckHost.Localization.AppStrings.Integrations.Steam.Actions;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.Steam.Errors;
using Params = MacroDeckHost.Localization.AppStrings.Integrations.Steam.Params;

namespace MacroDeckHost.Integrations.Steam.Actions;

internal static class SteamActions
{
	public const string GameParameter = "game";

	public static IReadOnlyList<IActionDefinition> Create(ISteamSource target) =>
	[
		new SteamAction("launch-game", ActionStrings.LaunchGame.Name(), ActionStrings.LaunchGame.Description(), target,
			SteamCommandKind.LaunchGame, withGame: true),
		new SteamAction("launch-steam", ActionStrings.LaunchSteam.Name(), ActionStrings.LaunchSteam.Description(),
			target, SteamCommandKind.LaunchSteam, withGame: false),
		new SteamAction("open-store-page", ActionStrings.OpenStorePage.Name(), ActionStrings.OpenStorePage.Description(),
			target, SteamCommandKind.OpenStorePage, withGame: true),
		new SteamAction("open-library-page", ActionStrings.OpenLibraryPage.Name(),
			ActionStrings.OpenLibraryPage.Description(), target, SteamCommandKind.OpenLibraryPage, withGame: true)
	];

	internal static ActionResult ToResult(SteamCommandOutcome outcome)
		=> outcome switch
		{
			SteamCommandOutcome.Succeeded => ActionResult.Success(),
			SteamCommandOutcome.InvalidAppId => ActionResult.Failed(ActionErrorCodes.InvalidParameter,
				Errors.InvalidGame()),
			SteamCommandOutcome.NotAvailable => ActionResult.Failed(ActionErrorCodes.Unavailable,
				Errors.NotAvailable()),
			_ => ActionResult.Failed(ActionErrorCodes.ProviderError, Errors.LaunchFailed())
		};
}

internal sealed class SteamAction : IDynamicOptionsActionDefinition
{
	private readonly ISteamSource _target;
	private readonly SteamCommandKind _kind;

	public SteamAction(
		string id,
		LocalizedText name,
		LocalizedText description,
		ISteamSource target,
		SteamCommandKind kind,
		bool withGame)
	{
		Id = id;
		Name = name;
		Description = description;
		_target = target;
		_kind = kind;
		Parameters = withGame
			?
			[
				ActionParameter.Autocomplete(SteamActions.GameParameter,
					label: Params.Game(),
					description: Params.GameDescription(),
					placeholder: Params.GamePlaceholder(),
					required: true)
			]
			: [];
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor() => new Executor(this);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var filter = context.Filter?.Trim();
		var games = _target.Snapshot.Games.Where(game => string.IsNullOrEmpty(filter) ||
			game.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

		return Task.FromResult(new DynamicOptionsResult
		{
			Options =
			[
				.. games.Select(game => new ActionParameterOption
				{
					Value = game.AppId.ToString(CultureInfo.InvariantCulture),
					Label = game.Name
				})
			],
			AllowsCustomValue = true,
			CacheSeconds = 0
		});
	}

	private sealed class Executor(SteamAction action) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var appId = 0;
			if (action._kind != SteamCommandKind.LaunchSteam)
			{
				var text = Convert.ToString(context.Parameters.GetValueOrDefault(SteamActions.GameParameter),
					CultureInfo.InvariantCulture)?.Trim();
				if (SteamLauncher.ParseAppId(text) is not { } parsed)
				{
					return SteamActions.ToResult(SteamCommandOutcome.InvalidAppId);
				}

				appId = parsed;
			}

			return SteamActions.ToResult(await action._target.ExecuteAsync(new SteamCommand(action._kind, appId),
				context.CancellationToken));
		}
	}
}
