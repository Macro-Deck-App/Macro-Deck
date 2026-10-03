using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Deck;

internal sealed class DeckActionDefinition : IActionDefinition
{
	private readonly Func<IDeckNavigator?> _navigator;
	private readonly Func<IDeckNavigator, string?, CancellationToken, Task> _invoke;

	public DeckActionDefinition(
		string id,
		LocalizedText name,
		LocalizedText description,
		Func<IDeckNavigator?> navigator,
		Func<IDeckNavigator, string?, CancellationToken, Task> invoke)
	{
		Id = id;
		Name = name;
		Description = description;
		_navigator = navigator;
		_invoke = invoke;
	}

	public string Id { get; }
	public LocalizedText Name { get; }
	public LocalizedText Description { get; }
	public IReadOnlyList<ActionParameter> Parameters { get; } = [DeckDeviceTarget.Parameter()];

	public IActionExecutor CreateExecutor() => new Executor(_navigator, _invoke);

	private sealed class Executor : IActionExecutor
	{
		private readonly Func<IDeckNavigator?> _navigator;
		private readonly Func<IDeckNavigator, string?, CancellationToken, Task> _invoke;

		public Executor(Func<IDeckNavigator?> navigator, Func<IDeckNavigator, string?, CancellationToken, Task> invoke)
		{
			_navigator = navigator;
			_invoke = invoke;
		}

		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var navigator = _navigator();
			if (navigator is null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable,
					AppStrings.Integrations.Deck.Errors.NavigationUnavailable());
			}

			if (!DeckDeviceTarget.TryResolve(navigator, context, out var originClientId, out var failure))
			{
				return failure!;
			}

			await _invoke(navigator, originClientId, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
