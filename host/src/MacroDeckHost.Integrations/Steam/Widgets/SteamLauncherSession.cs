using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Integrations.Steam.State;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.Steam.Errors;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal sealed class SteamLauncherSession : IUiSession
{
	public static readonly TimeSpan FailureDisplay = TimeSpan.FromSeconds(10);

	private readonly ISteamSource? _source;
	private readonly SteamLauncherOptions _options;
	private readonly SteamArtworkCache _artwork;
	private readonly UiResource? _glyph;
	private readonly TimeProvider _time;
	private readonly UiState<SteamLauncherState> _state;
	private readonly UiView _view;
	private readonly ITimer _timer;
	private readonly Lock _sync = new();
	private readonly Lock _dispatch = new();

	private IUiEventOrigin? _origin;
	private LocalizedText _failure;
	private DateTimeOffset _failureUntil;
	private bool _disposed;

	public SteamLauncherSession(
		UiSurface surface,
		ISteamSource? source,
		SteamLauncherOptions options,
		SteamArtworkCache artwork,
		UiResource? glyph,
		TimeProvider time,
		Func<UiState<SteamLauncherState>, Func<int, UiEventOutcome>, UiElement> build)
	{
		_source = source;
		_options = options;
		_artwork = artwork;
		_glyph = glyph;
		_time = time;
		_state = new UiState<SteamLauncherState>(Compute());
		_view = new UiView(surface, build(_state, Press));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_timer = time.CreateTimer(_ => Refresh(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

		if (_source is not null)
		{
			_source.Changed += OnChanged;
		}

		_artwork.Changed += OnChanged;
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
		Deliver(uiEvent, null);
	}

	public void Dispatch(UiEvent uiEvent, IUiEventOrigin origin)
	{
		Deliver(uiEvent, origin);
	}

	public ValueTask DisposeAsync()
	{
		if (_source is not null)
		{
			_source.Changed -= OnChanged;
		}

		_artwork.Changed -= OnChanged;

		lock (_sync)
		{
			_disposed = true;
		}

		_timer.Dispose();
		_view.Dispose();

		return ValueTask.CompletedTask;
	}

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

	private void OnChanged(object? sender, EventArgs args)
	{
		Refresh();
	}

	private UiEventOutcome Press(int appId)
	{
		if (_source is not { IsAvailable: true })
		{
			return UiEventOutcome.Rejected("Steam is not available.");
		}

		_ = RunAsync(appId, _origin);
		return UiEventOutcome.Accepted;
	}

	private async Task RunAsync(int appId, IUiEventOrigin? origin)
	{
		var outcome = await _source!.ExecuteAsync(new SteamCommand(SteamCommandKind.LaunchGame, appId),
				CancellationToken.None)
			.ConfigureAwait(false);

		if (outcome == SteamCommandOutcome.Succeeded)
		{
			return;
		}

		LocalizedText message = outcome switch
		{
			SteamCommandOutcome.NotAvailable => Errors.NotAvailable(),
			SteamCommandOutcome.InvalidAppId => Errors.InvalidGame(),
			_ => Errors.LaunchFailed()
		};

		lock (_sync)
		{
			_failure = message;
			_failureUntil = _time.GetUtcNow() + FailureDisplay;
		}

		Refresh();

		if (origin is not null)
		{
			await origin.ReportFailureAsync(message).ConfigureAwait(false);
		}
	}

	private SteamLauncherState Compute()
	{
		var failure = _time.GetUtcNow() < _failureUntil ? _failure : default;
		return SteamWidgetStates.Launcher(_source, _options, _artwork, _glyph, failure);
	}

	private void Refresh()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var next = Compute();
			if (next != _state.Value)
			{
				using (_view.Batch())
				{
					_state.Set(next);
				}
			}

			var now = _time.GetUtcNow();
			_timer.Change(now < _failureUntil ? _failureUntil - now : Timeout.InfiniteTimeSpan,
				Timeout.InfiniteTimeSpan);
		}
	}
}
