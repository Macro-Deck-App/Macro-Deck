using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Integrations.Ui;

internal sealed class UiViewSession : IUiSession
{
	private readonly UiView _view;
	private readonly Lock _dispatch = new();
	private Func<ValueTask>? _onDispose;
	private IUiEventOrigin? _origin;

	public UiViewSession(UiView view, Func<ValueTask>? onDispose = null)
	{
		ArgumentNullException.ThrowIfNull(view);

		_view = view;
		_onDispose = onDispose;
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiView View => _view;

	public IUiEventOrigin? Origin => _origin;

	public UiTree BuildTree()
	{
		return _view.Tree;
	}

	public IReadOnlyList<UiPatch> DrainPatches()
	{
		return _view.DrainPatches();
	}

	public void Dispatch(UiEvent uiEvent)
	{
		Deliver(uiEvent, null);
	}

	public void Dispatch(UiEvent uiEvent, IUiEventOrigin origin)
	{
		Deliver(uiEvent, origin);
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _onDispose, null) is { } onDispose)
		{
			await onDispose().ConfigureAwait(false);
		}

		_view.Dispose();
	}

	// Handlers run synchronously inside UiView.Dispatch, so a handler reads the origin of exactly the
	// event it is handling; the host never dispatches concurrently to one session.
	private void Deliver(UiEvent uiEvent, IUiEventOrigin? origin)
	{
		lock (_dispatch)
		{
			_origin = origin;
			try
			{
				_view.Dispatch(uiEvent);
			}
			finally
			{
				_origin = null;
			}
		}
	}
}
