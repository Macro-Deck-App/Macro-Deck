using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Email;

namespace MacroDeckHost.Widgets.EmailInbox;

internal sealed class EmailInboxWidgetSession : IUiSession
{
	private static readonly TimeSpan _minimumDelay = TimeSpan.FromSeconds(1);

	private static readonly TimeSpan _maximumDelay = TimeSpan.FromMinutes(5);

	private readonly IEmailInboxCache _cache;
	private readonly TimeProvider _time;
	private readonly Func<EmailInboxesSnapshot, DateTimeOffset, EmailInboxFormat, EmailInboxComputed> _compute;
	private readonly Func<Task<EmailInboxFormat?>> _resolveFormat;
	private readonly UiState<EmailInboxWidgetState> _state;
	private readonly UiView _view;
	private readonly ITimer _timer;
	private readonly Lock _sync = new();

	private EmailInboxFormat _format;
	private bool _disposed;

	public EmailInboxWidgetSession(
		UiSurface surface,
		IEmailInboxCache cache,
		TimeProvider time,
		EmailInboxFormat format,
		Func<Task<EmailInboxFormat?>> resolveFormat,
		Func<EmailInboxesSnapshot, DateTimeOffset, EmailInboxFormat, EmailInboxComputed> compute,
		Func<UiState<EmailInboxWidgetState>, UiElement> build)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(cache);
		ArgumentNullException.ThrowIfNull(time);
		ArgumentNullException.ThrowIfNull(format);
		ArgumentNullException.ThrowIfNull(resolveFormat);
		ArgumentNullException.ThrowIfNull(compute);
		ArgumentNullException.ThrowIfNull(build);

		_cache = cache;
		_time = time;
		_format = format;
		_resolveFormat = resolveFormat;
		_compute = compute;
		_state = new UiState<EmailInboxWidgetState>(compute(cache.Snapshot, time.GetUtcNow(), format).State);
		_view = new UiView(surface, build(_state));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_timer = time.CreateTimer(_ => _ = ResolveFormatAndRefreshAsync(),
			null,
			Timeout.InfiniteTimeSpan,
			Timeout.InfiniteTimeSpan);
		_cache.Changed += Refresh;

		// Also arms the timer, and a sync landing between the first compute and the subscription is not lost.
		Refresh();
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

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
		_view.Dispatch(uiEvent);
	}

	public ValueTask DisposeAsync()
	{
		_cache.Changed -= Refresh;

		lock (_sync)
		{
			_disposed = true;
		}

		_timer.Dispose();
		_view.Dispose();

		return ValueTask.CompletedTask;
	}

	private async Task ResolveFormatAndRefreshAsync()
	{
		if (await _resolveFormat().ConfigureAwait(false) is { } format)
		{
			lock (_sync)
			{
				_format = format;
			}
		}

		Refresh();
	}

	private void Refresh()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var now = _time.GetUtcNow();
			var computed = _compute(_cache.Snapshot, now, _format);

			using (_view.Batch())
			{
				_state.Set(computed.State);
			}

			var delay = computed.NextRefresh - now;
			_timer.Change(delay < _minimumDelay ? _minimumDelay : delay > _maximumDelay ? _maximumDelay : delay,
				Timeout.InfiniteTimeSpan);
		}
	}
}
