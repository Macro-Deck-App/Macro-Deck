using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Widgets.ScreenSavers;

internal sealed class DriftingScreenSaverSession : IUiSession
{
	public static readonly TimeSpan DriftInterval = TimeSpan.FromSeconds(60);

	private readonly IUiSession _inner;
	private readonly UiState<int> _position;
	private readonly ITimer _timer;

	public DriftingScreenSaverSession(IUiSession inner, UiState<int> position, TimeProvider timeProvider)
	{
		ArgumentNullException.ThrowIfNull(inner);
		ArgumentNullException.ThrowIfNull(position);
		ArgumentNullException.ThrowIfNull(timeProvider);

		_inner = inner;
		_position = position;
		_inner.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_inner.Faulted += (_, args) => Faulted?.Invoke(this, args);
		_timer = timeProvider.CreateTimer(_ => Drift(), null, DriftInterval, DriftInterval);
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _inner.BuildTree();

	public IReadOnlyList<UiPatch> DrainPatches() => _inner.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => _inner.Dispatch(uiEvent);

	public async ValueTask DisposeAsync()
	{
		_timer.Dispose();
		await _inner.DisposeAsync().ConfigureAwait(false);
	}

	// Always a different slot than the current one: a slow reposition that sometimes stays put would
	// let the face burn in for twice the interval.
	private void Drift()
	{
		var next = (_position.Value + 1 + Random.Shared.Next(ScreenSaverDrift.PositionCount - 1)) %
			ScreenSaverDrift.PositionCount;
		_position.Value = next;
	}
}
