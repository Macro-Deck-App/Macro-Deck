using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Deck;

internal sealed class ChangeFolderActionDefinition : IDynamicOptionsActionDefinition
{
	private readonly Func<IDeckNavigator?> _navigator;

	public ChangeFolderActionDefinition(Func<IDeckNavigator?> navigator)
	{
		_navigator = navigator;
	}

	public string Id => "change-folder";
	public LocalizedText Name => AppStrings.Integrations.Deck.Actions.ChangeFolderName();
	public LocalizedText Description => AppStrings.Integrations.Deck.Actions.ChangeFolderDescription();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.DynamicChoice("folderId",
			label: AppStrings.Integrations.Deck.Actions.FolderLabel(),
			description: AppStrings.Integrations.Deck.Actions.FolderDescription(),
			required: true)
	];

	public IActionExecutor CreateExecutor() => new Executor(_navigator);

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		var folders = _navigator()?.GetFolders() ?? [];
		var options = folders
			.Where(folder => context.Filter is null ||
				folder.Label.Contains(context.Filter, StringComparison.OrdinalIgnoreCase))
			.Select(folder => new ActionParameterOption { Value = folder.Id, Label = folder.Label })
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

			var folderId = context.Parameters.TryGetValue("folderId", out var value)
				? value.ToString() ?? string.Empty
				: string.Empty;

			if (string.IsNullOrEmpty(folderId))
			{
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.Deck.Errors.NoFolderSelected());
			}

			if (navigator.GetFolders().All(folder => folder.Id != folderId))
			{
				return ActionResult.Failed(ActionErrorCodes.NotFound,
					AppStrings.Integrations.Deck.Errors.FolderNotFound(folderId: folderId));
			}

			await navigator.ChangeFolderAsync(folderId, context.OriginClientId, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
