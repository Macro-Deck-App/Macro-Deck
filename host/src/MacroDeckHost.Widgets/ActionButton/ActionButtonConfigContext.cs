using MacroDeck.Localization;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Actions;

namespace MacroDeckHost.Widgets.ActionButton;

internal sealed class ActionButtonConfigContext(
	ActionProviderProbe probe,
	ILocalizationResolver localization,
	string? culture,
	TimeProvider timeProvider,
	CancellationToken sessionToken)
{
	public static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(400);

	private UiView? _view;

	public ActionProviderProbe Probe { get; } = probe;

	public ILocalizationResolver Localization { get; } = localization;

	public string? Culture { get; } = culture;

	public TimeProvider TimeProvider { get; } = timeProvider;

	public CancellationToken SessionToken { get; } = sessionToken;

	public string Resolve(LocalizedText text) => Localization.Resolve(text, Culture) ?? string.Empty;

	// The view is built from the tree this context helps produce, so it can only be attached afterwards.
	public void Attach(UiView view) => _view = view;

	public IDisposable Batch() => _view?.Batch() ?? NoBatch.Instance;

	private sealed class NoBatch : IDisposable
	{
		public static readonly NoBatch Instance = new();

		public void Dispose()
		{
		}
	}
}
