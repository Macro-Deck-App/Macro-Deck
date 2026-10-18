using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;
using MacroDeckHost.Integrations.Feeds.Connections;
using MacroDeckHost.Integrations.Feeds.Protocol;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Feeds;

namespace MacroDeckHost.Integrations.Feeds.Widgets;

internal sealed class FeedWidgetSession : IUiSession
{
	private static readonly ILogger _logger = IntegrationLog.For<FeedWidgetSession>(FeedsIntegration.IntegrationId);

	private readonly FeedWidgetOptions _options;
	private readonly IFeedSource _source;
	private readonly IFeedImages _images;
	private readonly TimeProvider _time;
	private readonly Guid? _widgetId;
	private readonly CultureInfo _culture;
	private readonly string? _cultureName;
	private readonly UiState<FeedWidgetState> _state;
	private readonly UiState<int> _index = new(0);
	private readonly UiState<UiResource?> _icon;
	private readonly UiView _view;
	private readonly ITimer _timer;
	private readonly Lock _sync = new();

	private Func<Task<UiResource?>>? _pendingIcon;
	private IUiEventOrigin? _origin;
	private bool _disposed;

	public FeedWidgetSession(
		UiSurface surface,
		FeedWidgetKind kind,
		FeedWidgetOptions options,
		IFeedSource source,
		IFeedImages images,
		TimeProvider time,
		UiResource? icon,
		Func<Task<UiResource?>>? pendingIcon)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(images);

		_options = options;
		_source = source;
		_images = images;
		_time = time;
		_widgetId = surface.Kind == UiSurfaceKinds.Widget ? UiWidgetSurface.WidgetId(surface) : null;
		_cultureName = UiWidgetSurface.Culture(surface);
		_culture = FeedDateFormat.CultureOf(_cultureName);
		_pendingIcon = pendingIcon;
		_icon = new UiState<UiResource?>(icon);
		_state = new UiState<FeedWidgetState>(Resolve());

		var data = UiWidgetSurface.Data(surface);
		_view = new UiView(surface,
			FeedWidgetView.Build(kind,
				_state,
				_index,
				_icon,
				options,
				Press,
				UiWidgetSafeArea.RadiusOf(surface),
				FeedWidgetOptions.BackgroundColor(data)));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		var cycle = kind == FeedWidgetKind.Featured && options.CycleSeconds > 0
			? TimeSpan.FromSeconds(options.CycleSeconds)
			: Timeout.InfiniteTimeSpan;
		_timer = time.CreateTimer(_ => Advance(), null, cycle, cycle);

		_source.Changed += OnChanged;
		_images.Changed += OnChanged;
		Refresh();
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	internal FeedWidgetState State => _state.Value;

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
		_source.Changed -= OnChanged;
		_images.Changed -= OnChanged;

		lock (_sync)
		{
			_disposed = true;
		}

		_timer.Dispose();
		_view.Dispose();
		return ValueTask.CompletedTask;
	}

	internal static ModalDefinition Dialog(FeedRow row, string? culture, bool canOpen)
	{
		var data = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[FeedArticleDialog.EntryKey] = JsonSerializer.SerializeToElement(row.EntryId.ToString("D")),
			[FeedArticleDialog.ItemKey] = JsonSerializer.SerializeToElement(row.Key),
			[FeedArticleDialog.CanOpenKey] = JsonSerializer.SerializeToElement(canOpen)
		};

		if (culture is not null)
		{
			data[FeedArticleDialog.CultureKey] = JsonSerializer.SerializeToElement(culture);
		}

		return new ModalDefinition
		{
			ViewId = FeedWidgetTypes.DialogViewId,
			Title = Strings.Dialog.Title(),
			Data = data
		};
	}

	private void Deliver(UiEvent uiEvent, IUiEventOrigin? origin)
	{
		lock (_sync)
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

	private UiEventOutcome Press(string rowId)
	{
		var row = _state.Value.Rows.FirstOrDefault(candidate => candidate.Id == rowId);
		var item = row is null ? null : _source.Find(row.EntryId)?.Find(row.Key);
		if (row is null || item is null)
		{
			return UiEventOutcome.Rejected("The article is no longer in the feed.");
		}

		_source.Open(row.EntryId, row.Key, _widgetId);

		var origin = _origin;
		if (origin is null || _options.Tap == FeedWidgetTypes.FlowsTap)
		{
			return UiEventOutcome.Accepted;
		}

		var url = Uri.TryCreate(item.Url, UriKind.Absolute, out var parsed) && FeedClient.IsWebUrl(parsed)
			? parsed
			: null;
		_ = _options.Tap == FeedWidgetTypes.OpenTap && url is not null
			? OpenAsync(origin, row, url)
			: ShowDialogAsync(origin, row, url is not null);
		return UiEventOutcome.Accepted;
	}

	private async Task OpenAsync(IUiEventOrigin origin, FeedRow row, Uri url)
	{
		try
		{
			if (await origin.OpenUrlAsync(url).ConfigureAwait(false))
			{
				return;
			}
		}
#pragma warning disable CA1031 // A press has nobody to report to; the dialog below still shows the link.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Warning(exception, "Opening a feed article on client {ClientId} failed", origin.ClientId);
		}

		await ShowDialogAsync(origin, row, false).ConfigureAwait(false);
	}

	private async Task ShowDialogAsync(IUiEventOrigin origin, FeedRow row, bool canOpen)
	{
		try
		{
			if (!await origin.ShowModalAsync(Dialog(row, _cultureName, canOpen)).ConfigureAwait(false))
			{
				_logger.Debug("The feed article dialog could not be opened on client {ClientId}", origin.ClientId);
			}
		}
#pragma warning disable CA1031 // A press has nobody to report to; the fault is logged instead of lost.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Warning(exception, "Opening the feed article dialog on client {ClientId} failed", origin.ClientId);
		}
	}

	private void OnChanged(object? sender, EventArgs args)
	{
		Refresh();
	}

	private void Advance()
	{
		lock (_sync)
		{
			if (_disposed || _state.Value.Rows.Count < 2)
			{
				return;
			}

			using (_view.Batch())
			{
				_index.Set((_index.Value + 1) % _state.Value.Rows.Count);
			}
		}
	}

	private FeedWidgetState Resolve()
	{
		var format = new FeedDateFormat(_culture, _time.LocalTimeZone, _time.GetUtcNow());
		return FeedWidgetResolver.Resolve(_options, _source.Feeds, _options.Count, format, _images.Find);
	}

	private void Refresh()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var next = Resolve();
			if (!next.Equals(_state.Value))
			{
				using (_view.Batch())
				{
					_state.Set(next);
					if (_index.Value >= next.Rows.Count)
					{
						_index.Set(0);
					}
				}
			}

			if (_options.ShowThumbnail)
			{
				foreach (var row in next.Rows.Where(candidate => candidate is { Image: null, ImageUrl: not null }))
				{
					var allowLocal = _source.Find(row.EntryId)?.AllowLocalNetwork == true;
					_images.Track(row.ImageUrl!, allowLocal);
				}
			}

			if (_pendingIcon is { } pending)
			{
				_pendingIcon = null;
				_ = ResolveIconAsync(pending);
			}
		}
	}

	private async Task ResolveIconAsync(Func<Task<UiResource?>> pending)
	{
		var icon = await pending().ConfigureAwait(false);
		if (icon is null)
		{
			return;
		}

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			using (_view.Batch())
			{
				_icon.Set(icon);
			}
		}
	}
}
