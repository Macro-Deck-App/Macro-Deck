using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Deck;

internal sealed class ChangeProfileActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly Func<IDeckNavigator?> _navigator;

	public ChangeProfileActionDefinition(Func<IDeckNavigator?> navigator)
	{
		_navigator = navigator;
	}

	public string Id => "change-profile";
	public LocalizedText Name => AppStrings.Integrations.Deck.Actions.ChangeProfileName();
	public LocalizedText Description => AppStrings.Integrations.Deck.Actions.ChangeProfileDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice("profileId",
			label: AppStrings.Integrations.Deck.Actions.ProfileLabel(),
			description: AppStrings.Integrations.Deck.Actions.ProfileDescription(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_navigator);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var profiles = _navigator()?.GetProfiles() ?? [];
		var options = profiles
			.Where(profile => context.Filter is null ||
				profile.Label.Contains(context.Filter, StringComparison.OrdinalIgnoreCase))
			.Select(profile => new ActionParameterOption { Value = profile.Id, Label = profile.Label })
			.ToList();

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = false,
			CacheSeconds = 2
		});
	}

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<IDeckNavigator?> _navigator;

		public Executor(Func<IDeckNavigator?> navigator)
		{
			_navigator = navigator;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var navigator = _navigator();
			if (navigator is null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.Deck.Errors.NavigationUnavailable());
			}

			var profileId = context.Parameters.TryGetValue("profileId", out var value)
				? value.ToString() ?? string.Empty
				: string.Empty;

			if (string.IsNullOrEmpty(profileId))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Deck.Errors.NoProfileSelected());
			}

			// ChangeProfileAsync is deliberately silent on a stale target (same as ChangeFolderAsync);
			// this check is what gives a missing target a visible runtime error instead.
			if (navigator.GetProfiles().All(profile => profile.Id != profileId))
			{
				return ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.Deck.Errors.ProfileNotFound(profileId: profileId));
			}

			await navigator.ChangeProfileAsync(profileId, context.OriginClientId, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
