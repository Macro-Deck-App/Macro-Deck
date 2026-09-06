using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Decks;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Deck;

[MacroDeckIntegration]
public sealed class DeckNavigationIntegration : IIntegration, ISystemIntegration
{
	public const string IntegrationId = "app.macro-deck.deck";

	private IDeckNavigator? _navigator;

	public DeckNavigationIntegration()
	{
		Actions =
		[
			new ChangeFolderActionDefinition(() => _navigator),
			new ChangeProfileActionDefinition(() => _navigator),
			new DeckActionDefinition("go-to-parent",
				AppStrings.Integrations.Deck.Actions.GoToParentName(),
				AppStrings.Integrations.Deck.Actions.GoToParentDescription(),
				() => _navigator,
				(navigator, context) => navigator.GoToParentAsync(context.OriginClientId, context.CancellationToken)),
			new DeckActionDefinition("go-back",
				AppStrings.Integrations.Deck.Actions.GoBackName(),
				AppStrings.Integrations.Deck.Actions.GoBackDescription(),
				() => _navigator,
				(navigator, context) => navigator.GoBackAsync(context.OriginClientId, context.CancellationToken))
		];
	}

	public string Id => IntegrationId;
	public LocalizedText Name => AppStrings.Integrations.Deck.Name();
	public string Version => "1.0.0";

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public bool IsActive => true;
	public bool IsInitialized => true;

	public Task InitializeAsync(IIntegrationContext context)
	{
		_navigator = context.Deck;
		return Task.CompletedTask;
	}

	public Task ShutdownAsync() => Task.CompletedTask;
}
