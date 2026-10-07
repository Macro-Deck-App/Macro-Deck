using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Domain.Enums;
using Serilog;

namespace MacroDeckHost.Application.Variables.Colors;

public sealed class PluginColorWatches : IDisposable
{
	public static readonly TimeSpan DefaultCoalescingWindow = TimeSpan.FromMilliseconds(50);

	private readonly IColorReferenceResolver _colors;
	private readonly IFolderCache _folders;
	private readonly IPluginSessionRegistry _sessions;
	private readonly ColorChangeSignal _signal;
	private readonly TimeProvider _time;
	private readonly TimeSpan _window;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly Dictionary<string, PluginWatches> _byPlugin = new(StringComparer.Ordinal);
	private long _revision;

	public PluginColorWatches(IColorReferenceResolver colors,
		IFolderCache folders,
		IPluginSessionRegistry sessions,
		ColorChangeSignal signal,
		ILogger logger,
		TimeProvider? time = null,
		TimeSpan? coalescingWindow = null)
	{
		_colors = colors;
		_folders = folders;
		_sessions = sessions;
		_signal = signal;
		_time = time ?? TimeProvider.System;
		_window = coalescingWindow ?? DefaultCoalescingWindow;
		_logger = logger.ForContext<PluginColorWatches>();
		_signal.Changed += OnChanged;
		_sessions.SessionEnded += OnSessionEnded;
	}

	public ColorChangeSignal Signal => _signal;

	public void Dispose()
	{
		_signal.Changed -= OnChanged;
		_sessions.SessionEnded -= OnSessionEnded;
	}

	// Built only on the reference grammar and the resolver, never on template rendering, so a plugin can
	// read Color variables and nothing else.
	public string? Resolve(string? value, string? widgetId)
	{
		if (value is null)
		{
			return null;
		}

		Guid? widget = null;
		if (widgetId is not null)
		{
			if (!Guid.TryParse(widgetId, out var parsed) ||
				!_folders.GetAllFolders().Any(folder => folder.Widgets.Any(candidate => candidate.Id == parsed)))
			{
				return null;
			}

			widget = parsed;
		}

		return ColorReference.TryParse(value, out var reference)
			? _colors.ResolveColor(reference,
				widget is null ? VariableScope.Global : VariableScope.Widget,
				widget?.ToString())?.ToString()
			: RgbaColor.Canonicalize(value);
	}

	public bool TrySetWatches(string pluginId, IReadOnlyList<ColorWatchDto> watches)
	{
		if (watches.Count > ProtocolLimits.MaxColorWatches)
		{
			return false;
		}

		lock (_gate)
		{
			if (watches.Count == 0)
			{
				_byPlugin.Remove(pluginId);
				return true;
			}

			if (!_byPlugin.TryGetValue(pluginId, out var state))
			{
				state = new PluginWatches();
				_byPlugin[pluginId] = state;
			}

			state.Watches = [.. watches.DistinctBy(watch => watch.WatchId, StringComparer.Ordinal)];
			state.LastSent = null;
		}

		Schedule(pluginId);
		return true;
	}

	public IReadOnlyList<ColorWatchDto> WatchesOf(string pluginId)
	{
		lock (_gate)
		{
			return _byPlugin.TryGetValue(pluginId, out var state) ? state.Watches : [];
		}
	}

	private void OnChanged()
	{
		List<string> plugins;
		lock (_gate)
		{
			plugins = [.. _byPlugin.Keys];
		}

		foreach (var pluginId in plugins)
		{
			Schedule(pluginId);
		}
	}

	// A plugin reconnecting opens its new session before the old one ends, so only the end of its last
	// session drops the table; the SDK sends the table again on every connect.
	private void OnSessionEnded(object? sender, PluginSessionEndedEventArgs e)
	{
		var stillConnected = _sessions.Snapshot()
			.Any(session => string.Equals(session.PluginId, e.PluginId, StringComparison.Ordinal) &&
				!string.Equals(session.SessionId, e.SessionId, StringComparison.Ordinal) &&
				session.State is PluginSessionState.Connected or PluginSessionState.Awaiting);

		if (!stillConnected)
		{
			lock (_gate)
			{
				_byPlugin.Remove(e.PluginId);
			}
		}
	}

	// Trailing edge: a burst of changes inside the window ends in one push carrying the latest values.
	private void Schedule(string pluginId)
	{
		lock (_gate)
		{
			if (!_byPlugin.TryGetValue(pluginId, out var state) || state.Scheduled)
			{
				return;
			}

			state.Scheduled = true;
		}

		_ = FlushLaterAsync(pluginId);
	}

	private async Task FlushLaterAsync(string pluginId)
	{
		try
		{
			await Task.Delay(_window, _time).ConfigureAwait(false);

			IReadOnlyList<ColorWatchDto> watches;
			Dictionary<string, string?>? lastSent;
			lock (_gate)
			{
				if (!_byPlugin.TryGetValue(pluginId, out var state))
				{
					return;
				}

				state.Scheduled = false;
				watches = state.Watches;
				lastSent = state.LastSent;
			}

			var values = watches.ToDictionary(watch => watch.WatchId,
				watch => Resolve(watch.Value, watch.WidgetId),
				StringComparer.Ordinal);

			if (lastSent is not null && Same(lastSent, values))
			{
				return;
			}

			var sent = await _sessions.SendToPlugin(pluginId, Envelope(values)).ConfigureAwait(false);

			lock (_gate)
			{
				if (sent && _byPlugin.TryGetValue(pluginId, out var state) && ReferenceEquals(state.Watches, watches))
				{
					state.LastSent = values;
				}
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "Pushing watched colours to {PluginId} failed", pluginId);
		}
	}

	private ProtocolEnvelope Envelope(Dictionary<string, string?> values)
		=> new()
		{
			Type = MessageTypes.HostState,
			Id = Guid.CreateVersion7().ToString(),
			Payload = JsonSerializer.SerializeToElement(new HostStatePayload
				{
					Api = HostApis.Colors,
					Data = JsonSerializer.SerializeToElement(new ColorWatchStateDto
						{
							Revision = Interlocked.Increment(ref _revision),
							Values = [.. values.Select(pair => new ColorWatchValueDto { WatchId = pair.Key, Color = pair.Value })]
						},
						PluginProtocolJson.Options)
				},
				PluginProtocolJson.Options)
		};

	private static bool Same(Dictionary<string, string?> left, Dictionary<string, string?> right)
		=> left.Count == right.Count &&
			left.All(pair => right.TryGetValue(pair.Key, out var other) &&
				string.Equals(pair.Value, other, StringComparison.Ordinal));

	private sealed class PluginWatches
	{
		public IReadOnlyList<ColorWatchDto> Watches { get; set; } = [];

		public Dictionary<string, string?>? LastSent { get; set; }

		public bool Scheduled { get; set; }
	}
}
