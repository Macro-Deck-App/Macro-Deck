using System.Globalization;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Integrations.Steam.Native;
using MacroDeckHost.Integrations.Steam.State;
using ActionStrings = MacroDeckHost.Localization.AppStrings.Integrations.Steam.Actions;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.Steam.Errors;
using Params = MacroDeckHost.Localization.AppStrings.Integrations.Steam.Params;

namespace MacroDeckHost.Integrations.Steam.Actions;

internal static class SteamActions
{
	public const string GameParameter = "game";

	public const string ForceParameter = "force";

	public static IReadOnlyList<IActionDefinition> Create(ISteamSource target)
	{
		return
		[
			new SteamAction("launch-game",
				ActionStrings.LaunchGame.Name(),
				ActionStrings.LaunchGame.Description(),
				target,
				SteamCommandKind.LaunchGame),
			new SteamAction("launch-steam",
				ActionStrings.LaunchSteam.Name(),
				ActionStrings.LaunchSteam.Description(),
				target,
				SteamCommandKind.LaunchSteam),
			new SteamAction("open-store-page",
				ActionStrings.OpenStorePage.Name(),
				ActionStrings.OpenStorePage.Description(),
				target,
				SteamCommandKind.OpenStorePage),
			new SteamAction("open-library-page",
				ActionStrings.OpenLibraryPage.Name(),
				ActionStrings.OpenLibraryPage.Description(),
				target,
				SteamCommandKind.OpenLibraryPage),
			new SteamAction("close-game",
				ActionStrings.CloseGame.Name(),
				ActionStrings.CloseGame.Description(),
				target,
				SteamCommandKind.CloseGame)
		];
	}

	internal static bool TakesGame(SteamCommandKind kind)
	{
		return kind is not (SteamCommandKind.LaunchSteam or SteamCommandKind.CloseGame);
	}

	internal static ActionResult ToResult(
		SteamCommandOutcome outcome,
		SteamCommandKind kind = SteamCommandKind.LaunchGame)
	{
		return outcome switch
		{
			SteamCommandOutcome.Succeeded => ActionResult.Success(),
			SteamCommandOutcome.InvalidAppId => ActionResult.Failed(ActionErrorCodes.InvalidParameter,
				Errors.InvalidGame()),
			SteamCommandOutcome.NotAvailable when kind == SteamCommandKind.CloseGame => ActionResult.Failed(
				ActionErrorCodes.Unavailable,
				Errors.CloseNotSupported()),
			SteamCommandOutcome.NotAvailable => ActionResult.Failed(ActionErrorCodes.Unavailable,
				Errors.NotAvailable()),
			SteamCommandOutcome.NotRunning => ActionResult.Failed(ActionErrorCodes.Unavailable, Errors.NoGameRunning()),
			_ when kind == SteamCommandKind.CloseGame => ActionResult.Failed(ActionErrorCodes.ProviderError,
				Errors.CloseFailed()),
			_ => ActionResult.Failed(ActionErrorCodes.ProviderError, Errors.LaunchFailed())
		};
	}
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
		SteamCommandKind kind)
	{
		Id = id;
		Name = name;
		Description = description;
		_target = target;
		_kind = kind;
		Parameters = kind switch
		{
			SteamCommandKind.CloseGame =>
			[
				ActionParameter.Toggle(SteamActions.ForceParameter,
					Params.Force(),
					Params.ForceDescription())
			],
			_ when SteamActions.TakesGame(kind) =>
			[
				ActionParameter.Autocomplete(SteamActions.GameParameter,
					Params.Game(),
					Params.GameDescription(),
					placeholder: Params.GamePlaceholder(),
					required: true)
			],
			_ => []
		};
	}

	public string Id { get; }

	public LocalizedText Name { get; }

	public LocalizedText Description { get; }

	public IReadOnlyList<ActionParameter> Parameters { get; }

	public IActionExecutor CreateExecutor()
	{
		return new Executor(this);
	}

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var filter = context.Filter?.Trim();
		var games = _target.Snapshot.Games.Where(game
			=> string.IsNullOrEmpty(filter) || game.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase));

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

	private static bool ReadFlag(object? value)
	{
		return value switch
		{
			bool flag => flag,
			JsonElement { ValueKind: JsonValueKind.True } => true,
			string text => bool.TryParse(text.Trim(), out var parsed) && parsed,
			_ => false
		};
	}

	private sealed class Executor(SteamAction action) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var appId = 0;
			if (SteamActions.TakesGame(action._kind))
			{
				var text = Convert.ToString(context.Parameters.GetValueOrDefault(SteamActions.GameParameter),
						CultureInfo.InvariantCulture)
					?.Trim();
				if (SteamLauncher.ParseAppId(text) is not { } parsed)
				{
					return SteamActions.ToResult(SteamCommandOutcome.InvalidAppId, action._kind);
				}

				appId = parsed;
			}

			var force = action._kind == SteamCommandKind.CloseGame
				&& ReadFlag(context.Parameters.GetValueOrDefault(SteamActions.ForceParameter));

			return SteamActions.ToResult(await action._target.ExecuteAsync(new SteamCommand(action._kind, appId, force),
					context.CancellationToken),
				action._kind);
		}
	}
}
