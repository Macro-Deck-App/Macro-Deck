using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using Microsoft.Extensions.DependencyInjection;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Devices.Surfaces.KeyImages;

public sealed class DeviceKeyImageSession : IAsyncDisposable
{
	private static readonly TimeSpan _treeDeadline = TimeSpan.FromSeconds(1);

	private static readonly TimeSpan _reopenDelay = TimeSpan.FromSeconds(5);

	private static readonly DeviceKeyImageAppearance _defaultAppearance = new("dark", null, null);

	private const string DefaultCulture = "en";

	private const string WidgetLeftReason = "The widget is no longer on the device's keys.";

	private const string SessionEndedReason = "The device session ended.";

	private readonly Guid _deviceId;
	private readonly DeviceKeyImageLayout _layout;
	private readonly IDeviceSurfaceProvider _provider;
	private readonly IDeviceKeyImageRenderer _renderer;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly TimeSpan _minInterval;
	private readonly DeviceKeyImageResources _resources;
	private readonly DeviceKeyImagePresses _presses;
	private readonly string _principal;

	private readonly Lock _gate = new();
	private readonly SemaphoreSlim _passGate = new(1, 1);
	private readonly CancellationTokenSource _stopping = new();
	private readonly TaskCompletionSource<bool> _firstRender = new(TaskCreationOptions.RunContinuationsAsynchronously);
	private readonly Dictionary<string, WidgetView> _widgets = new(StringComparer.Ordinal);
	private readonly DeviceKeyImage?[,] _cells;
	private readonly long[,] _revisions;
	private readonly long[,] _pushedRevisions;

	private Dictionary<string, RenderedUnit> _rendered = new(StringComparer.Ordinal);
	private Dictionary<string, DateTimeOffset> _holds = new(StringComparer.Ordinal);
	private DeviceSurfaceProjection? _projection;
	private RenderEnvironment? _environment;
	private int _environmentVersion;
	private int _resourceVersion;
	private int _surfaceHolds;
	private bool _online;
	private bool _started;
	private bool _forceRender;
	private bool _forcePush;
	private bool _requested;
	private bool _disposed;
	private DateTimeOffset? _lastPassAt;
	private CancellationTokenSource? _pacing;
	private ITimer? _tick;

	public DeviceKeyImageSession(
		Guid deviceId,
		DeviceKeyImageLayout layout,
		IDeviceSurfaceProvider provider,
		IDeviceKeyImageRenderer renderer,
		IServiceScopeFactory scopeFactory,
		TimeProvider timeProvider,
		bool online,
		ILogger logger)
	{
		_deviceId = deviceId;
		_layout = layout;
		_provider = provider;
		_renderer = renderer;
		_scopeFactory = scopeFactory;
		_timeProvider = timeProvider;
		_online = online;
		_logger = logger;
		_minInterval = TimeSpan.FromSeconds(1.0 / layout.MaxUpdatesPerSecond);
		_principal = $"device-render:{deviceId:N}";
		_cells = new DeviceKeyImage?[layout.Rows, layout.Columns];
		_revisions = new long[layout.Rows, layout.Columns];
		_pushedRevisions = new long[layout.Rows, layout.Columns];
		_resources = new DeviceKeyImageResources(scopeFactory,
			new IconAppearanceContext(null,
				layout.AnimatedIcons ? IconAppearanceTraits.Animated : IconAppearanceTraits.Static),
			timeProvider,
			logger);
		_presses = new DeviceKeyImagePresses(renderer.PressMinVisible);
	}

	public DeviceKeyImageLayout Layout => _layout;

	public void Update(DeviceSurfaceProjection projection)
	{
		var opened = new List<WidgetView>();
		var closed = new List<WidgetView>();
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_projection = projection;
			var visible = new Dictionary<string, DeviceSurfaceRenderWidget>(StringComparer.Ordinal);
			foreach (var widget in VisibleWidgets(projection))
			{
				visible.TryAdd(widget.Id, widget);
			}

			foreach (var (id, view) in _widgets)
			{
				if (!visible.TryGetValue(id, out var widget)
					|| !string.Equals(widget.Type, view.Type, StringComparison.Ordinal))
				{
					closed.Add(view);
				}
			}

			foreach (var view in closed)
			{
				view.Removed = true;
				_widgets.Remove(view.WidgetId);
			}

			_presses.Retain(visible.Keys.ToHashSet(StringComparer.Ordinal));

			foreach (var widget in visible.Values)
			{
				if (!_widgets.ContainsKey(widget.Id))
				{
					var view = new WidgetView(widget.Id, widget.Type);
					_widgets[widget.Id] = view;
					opened.Add(view);
				}
			}
		}

		foreach (var view in closed)
		{
			CloseView(view, WidgetLeftReason);
		}

		foreach (var view in opened)
		{
			view.Deadline = _timeProvider.CreateTimer(_ => view.FirstTree.TrySetResult(),
				null,
				_treeDeadline,
				Timeout.InfiniteTimeSpan);
			OpenView(view);
		}

		Request();
	}

	public void Start()
	{
		lock (_gate)
		{
			if (_started)
			{
				return;
			}

			_started = true;
			_lastPassAt = null;
			_pacing?.Cancel();
		}

		Request();
	}

	public void SetOnline(bool online)
	{
		lock (_gate)
		{
			_online = online;
			if (!online)
			{
				_presses.Clear();
				return;
			}

			_forceRender = true;
			_forcePush = true;
			_lastPassAt = null;
			_pacing?.Cancel();
		}

		Request();
	}

	public void HoldForSurface()
	{
		lock (_gate)
		{
			_surfaceHolds++;
		}
	}

	public void ReleaseForSurface()
	{
		lock (_gate)
		{
			if (_surfaceHolds == 0)
			{
				return;
			}

			_surfaceHolds--;
		}

		Request();
	}

	public void Press(string widgetId)
	{
		lock (_gate)
		{
			_presses.Press(widgetId, _timeProvider.GetUtcNow());
		}

		RequestAside();
	}

	public void Release(string widgetId)
	{
		lock (_gate)
		{
			if (!_presses.Release(widgetId))
			{
				return;
			}
		}

		RequestAside();
	}

	public void Flash(string widgetId)
	{
		lock (_gate)
		{
			_presses.Press(widgetId, _timeProvider.GetUtcNow());
			_presses.Release(widgetId);
		}

		RequestAside();
	}

	public void Refresh()
	{
		lock (_gate)
		{
			_environmentVersion++;
		}

		Request();
	}

	private DeviceKeyImage? Get(int row, int column)
	{
		if (!Contains(row, column))
		{
			return null;
		}

		lock (_gate)
		{
			return _cells[row, column];
		}
	}

	public async Task<DeviceKeyImage?> GetAsync(int row, int column, CancellationToken cancellationToken)
	{
		if (!Contains(row, column))
		{
			return null;
		}

		return await _firstRender.Task.WaitAsync(cancellationToken) ? Get(row, column) : null;
	}

	public async ValueTask DisposeAsync()
	{
		List<WidgetView> views;
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			_disposed = true;
			views = [.. _widgets.Values];
			_widgets.Clear();
			foreach (var view in views)
			{
				view.Removed = true;
			}
		}

		await _stopping.CancelAsync();
		_tick?.Dispose();
		_firstRender.TrySetResult(false);
		foreach (var view in views)
		{
			CloseView(view, SessionEndedReason);
		}
	}

	private bool Contains(int row, int column)
	{
		return row >= 0 && row < _layout.Rows && column >= 0 && column < _layout.Columns;
	}

	private IEnumerable<DeviceSurfaceRenderWidget> VisibleWidgets(DeviceSurfaceProjection projection)
	{
		return projection.ProfileId is null
			? []
			: projection.RenderWidgets.Where(widget => widget.Column < _layout.Columns
				&& widget.Row < _layout.Rows
				&& widget.Column + widget.Columns > 0
				&& widget.Row + widget.Rows > 0);
	}

	private void OpenView(WidgetView view)
	{
		try
		{
			using var scope = _scopeFactory.CreateScope();
			var broker = scope.ServiceProvider.GetRequiredService<IUiSessionBroker>();
			var ticket = scope.ServiceProvider.GetRequiredService<IWidgetUiSessionOpener>()
				.Open(new OpenWidgetUiSessionRequest { WidgetId = view.WidgetId }, _principal, false);
			if (!ticket.Accepted)
			{
				view.FirstTree.TrySetResult();
				return;
			}

			int generation;
			lock (_gate)
			{
				if (view.Removed)
				{
					_ = broker.CloseAsync(ticket.SessionId, WidgetLeftReason, CancellationToken.None);
					return;
				}

				generation = ++view.Generation;
				view.SessionId = ticket.SessionId;
			}

			var tree = UiHostTree.Observe(broker,
				ticket.SessionId,
				document => OnTree(view, generation, document),
				(_, _, retryable) => OnEnded(view, generation, retryable),
				_logger);
			lock (_gate)
			{
				if (tree is not null && view.Generation == generation && !view.Removed)
				{
					view.Tree = tree;
					return;
				}
			}

			tree?.Dispose();
			view.FirstTree.TrySetResult();
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception,
				"Could not open the tree of widget {WidgetId} for device {DeviceId}'s keys",
				view.WidgetId,
				_deviceId);
			view.FirstTree.TrySetResult();
		}
	}

	private void CloseView(WidgetView view, string reason)
	{
		UiHostTree? tree;
		string? sessionId;
		lock (_gate)
		{
			tree = view.Tree;
			sessionId = view.SessionId;
			view.Tree = null;
			view.SessionId = null;
			view.Generation++;
		}

		view.Deadline?.Dispose();
		view.Reopen?.Dispose();
		view.FirstTree.TrySetResult();
		tree?.Dispose();
		if (sessionId is null)
		{
			return;
		}

		try
		{
			using var scope = _scopeFactory.CreateScope();
			_ = scope.ServiceProvider.GetRequiredService<IUiSessionBroker>()
				.CloseAsync(sessionId, reason, CancellationToken.None);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "Could not close the tree session {SessionId} of a device key", sessionId);
		}
	}

	private void OnTree(WidgetView view, int generation, JsonElement document)
	{
		lock (_gate)
		{
			if (view.Removed || view.Generation != generation)
			{
				return;
			}

			view.Current = document;
			view.TreeVersion++;
			view.Delivered = true;
		}

		view.FirstTree.TrySetResult();
		_resources.ForgetAbsent();
		Request();
	}

	private void OnEnded(WidgetView view, int generation, bool retryable)
	{
		bool delivered;
		UiHostTree? ended;
		lock (_gate)
		{
			if (view.Removed || view.Generation != generation || _disposed)
			{
				return;
			}

			view.Generation++;
			view.SessionId = null;
			ended = view.Tree;
			view.Tree = null;
			delivered = view.Delivered;
			view.Delivered = false;
		}

		ended?.Dispose();
		view.FirstTree.TrySetResult();
		if (!retryable)
		{
			return;
		}

		if (delivered)
		{
			_ = Task.Run(() => OpenView(view));
			return;
		}

		view.Reopen?.Dispose();
		view.Reopen = _timeProvider.CreateTimer(_ => OpenView(view), null, _reopenDelay, Timeout.InfiniteTimeSpan);
	}

	private void Request()
	{
		lock (_gate)
		{
			if (_disposed || _requested)
			{
				return;
			}

			_requested = true;
		}

		_ = RunAsync();
	}

	// A pass with nothing to wait for renders on the calling thread, and a press must not wait for it.
	private void RequestAside()
	{
		_ = Task.Run(Request);
	}

	private async Task RunAsync()
	{
		var token = _stopping.Token;
		try
		{
			await _passGate.WaitAsync(token);
		}
		catch (OperationCanceledException)
		{
			return;
		}

		try
		{
			while (true)
			{
				CancellationTokenSource pacing;
				TimeSpan wait;
				lock (_gate)
				{
					if (!_requested || _disposed)
					{
						return;
					}

					_requested = false;
					wait = _lastPassAt is { } last ? last + _minInterval - _timeProvider.GetUtcNow() : TimeSpan.Zero;
					pacing = _pacing = CancellationTokenSource.CreateLinkedTokenSource(token);
				}

				try
				{
					if (wait > TimeSpan.Zero)
					{
						await Task.Delay(wait, _timeProvider, pacing.Token);
					}
				}
				catch (OperationCanceledException) when (!token.IsCancellationRequested)
				{
					// The device can be pushed to now, so the pass it waited for runs at once.
				}
				finally
				{
					lock (_gate)
					{
						if (_pacing == pacing)
						{
							_pacing = null;
						}
					}

					pacing.Dispose();
				}

				await PassAsync(token);
			}
		}
		catch (OperationCanceledException) when (token.IsCancellationRequested)
		{
			// The session ended while a pass was waiting.
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception, "Failed to draw the keys of device {DeviceId}", _deviceId);
		}
		finally
		{
			_passGate.Release();
		}
	}

	private async Task PassAsync(CancellationToken cancellationToken)
	{
		Task[] pending;
		lock (_gate)
		{
			if (_projection is null)
			{
				return;
			}

			pending = [.. _widgets.Values.Select(view => view.FirstTree.Task).Where(task => !task.IsCompleted)];
		}

		if (pending.Length > 0)
		{
			await Task.WhenAll(pending).WaitAsync(cancellationToken);
		}

		var environment = await EnvironmentAsync(cancellationToken);
		List<RenderUnit> units;
		bool forceRender;
		var pressed = new Dictionary<string, string>(StringComparer.Ordinal);
		lock (_gate)
		{
			if (_projection is not { } projection)
			{
				return;
			}

			units = UnitsOf(projection, environment.Version, pressed);
			forceRender = _forceRender;
			_forceRender = false;
		}

		var now = _timeProvider.GetUtcNow();
		lock (_gate)
		{
			if (_surfaceHolds == 0)
			{
				_lastPassAt = now;
			}
		}

		var rendered = new Dictionary<string, RenderedUnit>(StringComparer.Ordinal);
		var shown = new List<string>();
		var holds = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
		var heldResources = new List<(string ResourceId, int? DisplayPx)>();
		var missing = new List<DeviceKeyImageResourceRequest>();
		var pictures = new (ReadOnlyMemory<byte> Content, string ContentType)?[_layout.Rows, _layout.Columns];
		foreach (var unit in units)
		{
			if (Render(unit, environment, now, forceRender, missing) is not { } result)
			{
				continue;
			}

			if (!result.Complete
				&& _rendered.GetValueOrDefault(unit.Id) is { Complete: true } previous
				&& !ReferenceEquals(previous, result)
				&& SameShape(previous, result))
			{
				var since = _holds.GetValueOrDefault(unit.Id, now);
				if (now - since < _treeDeadline)
				{
					holds[unit.Id] = since;
					heldResources.AddRange(result.Resources);
					result = previous;
				}
			}

			rendered[unit.Id] = result;
			if (result.Key == unit.Key && pressed.TryGetValue(unit.Id, out var pressedWidgetId))
			{
				shown.Add(pressedWidgetId);
			}

			foreach (var picture in result.Keys)
			{
				var row = unit.Row + picture.RowOffset;
				var column = unit.Column + picture.ColumnOffset;
				if (Contains(row, column))
				{
					pictures[row, column] = (picture.Content, picture.ContentType);
				}
			}
		}

		_rendered = rendered;
		_holds = holds;
		_resources.Retain(rendered.Values.SelectMany(unit => unit.Resources).Concat(heldResources));
		var changed = new List<DeviceKeyImage>();
		bool pushing;
		DateTimeOffset? pressExpiry;
		lock (_gate)
		{
			pushing = _online && _started && _surfaceHolds == 0;
			if (pushing)
			{
				_presses.Shown(shown, now);
			}

			pressExpiry = _presses.NextExpiry;
			var forcePush = pushing && _forcePush;
			if (pushing)
			{
				_forcePush = false;
			}

			for (var row = 0; row < _layout.Rows; row++)
			{
				for (var column = 0; column < _layout.Columns; column++)
				{
					if (pictures[row, column] is not { } picture)
					{
						continue;
					}

					var current = _cells[row, column];
					if (current is null
						|| current.ContentType != picture.ContentType
						|| !current.Content.Span.SequenceEqual(picture.Content.Span)
						|| forcePush)
					{
						current = new DeviceKeyImage
						{
							Row = row,
							Column = column,
							Revision = ++_revisions[row, column],
							ContentType = picture.ContentType,
							Content = picture.Content
						};
						_cells[row, column] = current;
					}

					if (pushing && _pushedRevisions[row, column] < current.Revision)
					{
						_pushedRevisions[row, column] = current.Revision;
						changed.Add(current);
					}
				}
			}
		}

		_firstRender.TrySetResult(true);
		if (changed.Count > 0)
		{
			try
			{
				await _provider.PushKeyImagesAsync(_deviceId.ToString(), changed, cancellationToken);
			}
			catch (Exception exception) when (exception is not (OutOfMemoryException or OperationCanceledException))
			{
				_logger.Error(exception, "Failed to push key images to device {DeviceId}", _deviceId);
			}
		}

		if (missing.Count > 0)
		{
			_ = FetchAsync(missing);
		}

		var next = pushing
			? rendered.Where(unit => !holds.ContainsKey(unit.Key))
				.Select(unit => unit.Value.NextChangeAt)
				.Append(pressExpiry)
				.Where(at => at is not null)
				.Min()
			: null;
		foreach (var since in holds.Values)
		{
			var expiry = since + _treeDeadline;
			next = next is { } at && at < expiry ? at : expiry;
		}

		Schedule(next, now);
	}

	private RenderedUnit? Render(
		RenderUnit unit,
		RenderEnvironment environment,
		DateTimeOffset now,
		bool force,
		List<DeviceKeyImageResourceRequest> missing)
	{
		var cached = _rendered.GetValueOrDefault(unit.Id);
		if (!force && cached is not null && cached.Key == unit.Key && !(cached.NextChangeAt <= now))
		{
			return cached;
		}

		try
		{
			var resources = new UsedResources(_resources);
			var result = _renderer.Render(unit.Request with
			{
				Appearance = environment.Appearance,
				Culture = environment.Culture,
				TimeZone = _timeProvider.LocalTimeZone,
				Now = now,
				Resources = resources
			});
			missing.AddRange(result.MissingResources);
			return new RenderedUnit(unit.Key, result.NextChangeAt, result.Keys, resources.Used, !resources.Pending);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception, "Failed to draw key {Unit} of device {DeviceId}", unit.Id, _deviceId);
			return cached;
		}
	}

	private static bool SameShape(RenderedUnit previous, RenderedUnit result)
	{
		return previous.Keys
			.Select(key => (key.RowOffset, key.ColumnOffset))
			.ToHashSet()
			.SetEquals(result.Keys.Select(key => (key.RowOffset, key.ColumnOffset)));
	}

	private List<RenderUnit> UnitsOf(
		DeviceSurfaceProjection projection,
		int environmentVersion,
		Dictionary<string, string> pressed)
	{
		var now = _timeProvider.GetUtcNow();
		var layout = projection.Surface.Layout;
		var hasSurface = projection.ProfileId is not null;
		var radius = _layout.CornerRadiusFor(layout.WidgetBorderRadius, hasSurface);
		var spacing = _layout.WidgetSpacing && hasSurface ? layout.WidgetSpacing : 0;
		var background = hasSurface ? layout.BackgroundColor : null;
		var shared = string.Join('|',
			background,
			radius.ToString(CultureInfo.InvariantCulture),
			spacing.ToString(CultureInfo.InvariantCulture),
			projection.EmptyCellsTransparent,
			environmentVersion.ToString(CultureInfo.InvariantCulture),
			_resourceVersion.ToString(CultureInfo.InvariantCulture));
		var template = new DeviceKeyImageRenderRequest
		{
			KeySize = _layout.KeySize,
			Format = _layout.Format,
			Kind = DeviceKeyCellKind.Background,
			BackgroundColor = background,
			CornerRadius = radius,
			Spacing = spacing,
			EmptyCellVisible = !projection.EmptyCellsTransparent,
			Borders = _layout.Borders,
			AnimatedIcons = _layout.AnimatedIcons,
			Appearance = _defaultAppearance,
			Culture = DefaultCulture,
			TimeZone = TimeZoneInfo.Utc,
			Now = DateTimeOffset.UnixEpoch
		};

		var units = new List<RenderUnit>();
		var covered = new bool[_layout.Rows, _layout.Columns];
		foreach (var widget in VisibleWidgets(projection))
		{
			var view = _widgets.GetValueOrDefault(widget.Id);
			var unitId = $"w:{widget.Id}";
			var isPressed = _presses.IsPressed(widget.Id, now);
			if (isPressed)
			{
				pressed[unitId] = widget.Id;
			}

			units.Add(new RenderUnit(unitId,
				string.Join('|',
					"widget",
					widget.Type,
					widget.Columns.ToString(CultureInfo.InvariantCulture),
					widget.Rows.ToString(CultureInfo.InvariantCulture),
					widget.Data.GetRawText(),
					(view?.TreeVersion ?? 0).ToString(CultureInfo.InvariantCulture),
					isPressed ? "pressed" : "resting",
					shared),
				widget.Row,
				widget.Column,
				template with
				{
					Kind = DeviceKeyCellKind.Widget,
					Columns = Math.Max(1, widget.Columns),
					Rows = Math.Max(1, widget.Rows),
					WidgetType = widget.Type,
					WidgetData = widget.Data,
					Tree = view?.Current,
					Pressed = isPressed
				}));
			for (var row = Math.Max(0, widget.Row); row < Math.Min(_layout.Rows, widget.Row + widget.Rows); row++)
			{
				for (var column = Math.Max(0, widget.Column);
					column < Math.Min(_layout.Columns, widget.Column + widget.Columns);
					column++)
				{
					covered[row, column] = true;
				}
			}
		}

		for (var row = 0; row < _layout.Rows; row++)
		{
			for (var column = 0; column < _layout.Columns; column++)
			{
				if (covered[row, column])
				{
					continue;
				}

				var kind = hasSurface && row < layout.Rows && column < layout.Columns
					? DeviceKeyCellKind.Empty
					: DeviceKeyCellKind.Background;
				units.Add(new RenderUnit(string.Create(CultureInfo.InvariantCulture, $"c:{row},{column}"),
					$"{kind}|{shared}",
					row,
					column,
					template with { Kind = kind }));
			}
		}

		return units;
	}

	private async Task<RenderEnvironment> EnvironmentAsync(CancellationToken cancellationToken)
	{
		int version;
		lock (_gate)
		{
			version = _environmentVersion;
			if (_environment is { } current && current.Version == version)
			{
				return current;
			}
		}

		var appearance = _defaultAppearance;
		var culture = DefaultCulture;
		await using (var scope = _scopeFactory.CreateAsyncScope())
		{
			var preferences = scope.ServiceProvider.GetService<IAppPreferenceService>();
			try
			{
				if (preferences is not null)
				{
					var stored = await preferences.GetAppearance().WaitAsync(cancellationToken);
					appearance = new DeviceKeyImageAppearance(stored.ThemeMode, stored.AccentColor, stored.FontFamily);
				}
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				_logger.Warning(exception, "Device keys are drawn in the default appearance");
			}

			try
			{
				if (preferences is not null)
				{
					culture = (await preferences.GetLocalization().WaitAsync(cancellationToken)).Culture;
				}
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				_logger.Warning(exception, "Device keys are drawn in the default culture");
			}
		}

		var environment = new RenderEnvironment(version, appearance, culture);
		lock (_gate)
		{
			_environment = environment;
		}

		return environment;
	}

	private async Task FetchAsync(IReadOnlyList<DeviceKeyImageResourceRequest> missing)
	{
		try
		{
			var fetched = await _resources.FetchAsync(missing, _stopping.Token);
			if (fetched.Found)
			{
				lock (_gate)
				{
					_resourceVersion++;
				}
			}

			if (fetched.Found || fetched.Unblocked)
			{
				Request();
			}
		}
		catch (OperationCanceledException)
		{
			// The session ended while the resources were fetched.
		}
	}

	private void Schedule(DateTimeOffset? next, DateTimeOffset now)
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return;
			}

			var due = Timeout.InfiniteTimeSpan;
			if (next is { } at)
			{
				due = at > now ? at - now : TimeSpan.Zero;
			}

			_tick ??= _timeProvider.CreateTimer(_ => Request(),
				null,
				Timeout.InfiniteTimeSpan,
				Timeout.InfiniteTimeSpan);
			_tick.Change(due, Timeout.InfiniteTimeSpan);
		}
	}

	private sealed record RenderEnvironment(int Version, DeviceKeyImageAppearance Appearance, string Culture);

	private sealed record RenderUnit(string Id, string Key, int Row, int Column, DeviceKeyImageRenderRequest Request);

	private sealed record RenderedUnit(
		string Key,
		DateTimeOffset? NextChangeAt,
		IReadOnlyList<DeviceKeyImagePicture> Keys,
		IReadOnlyList<(string ResourceId, int? DisplayPx)> Resources,
		bool Complete);

	private sealed class UsedResources : IDeviceKeyImageResourceLookup
	{
		private readonly DeviceKeyImageResources _resources;

		public UsedResources(DeviceKeyImageResources resources)
		{
			_resources = resources;
		}

		public List<(string ResourceId, int? DisplayPx)> Used { get; } = [];

		public bool Pending { get; private set; }

		public DeviceKeyImageResource? TryGet(string resourceId, int? displayPx)
		{
			Used.Add((resourceId, displayPx));
			var resource = _resources.Lookup(resourceId, displayPx, out var pending);
			Pending |= pending;
			return resource;
		}
	}

	private sealed class WidgetView
	{
		public WidgetView(string widgetId, string type)
		{
			WidgetId = widgetId;
			Type = type;
		}

		public string WidgetId { get; }

		public string Type { get; }

		public TaskCompletionSource FirstTree { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public ITimer? Deadline { get; set; }

		public ITimer? Reopen { get; set; }

		public string? SessionId { get; set; }

		public UiHostTree? Tree { get; set; }

		public JsonElement? Current { get; set; }

		public int TreeVersion { get; set; }

		public int Generation { get; set; }

		public bool Delivered { get; set; }

		public bool Removed { get; set; }
	}
}
