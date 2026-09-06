using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Application.Rendering;

/// <summary>
/// In-process fan-out of the three client pushes a widget's live state already produces -
/// <see cref="WidgetStatePublisher.PublishIfChanged" />, <c>LabelRenderBackgroundService.Process</c> and
/// <c>WidgetUpdatedNotificationHandler.Handle</c> - so an open <c>IUiSession</c> for that widget can patch
/// its own tree from exactly the same computed values a legacy client is pushed, without a second poller or
/// a duplicated evaluation.
///
/// <para>
/// Raised beside the existing <c>SendToGroup</c>/enqueue call at each site, inheriting that call site's own
/// gating - a session that subscribes never sees more than a legacy client subscribed to the same group
/// would.
/// </para>
/// </summary>
public interface IWidgetRenderSignals
{
	/// <summary>Subscribes to every active-state push for <paramref name="widgetId" />. Dispose the result
	/// to unsubscribe.</summary>
	IDisposable SubscribeState(string widgetId, Action<WidgetStateUpdatedEvent> handler);

	/// <summary>Subscribes to every resolved-label push for <paramref name="widgetId" />, across every
	/// state - a session filters to the state it currently cares about itself.</summary>
	IDisposable SubscribeLabel(string widgetId, Action<LabelTextUpdatedEvent> handler);

	/// <summary>Subscribes to every stored-data change for <paramref name="widgetId" />, carrying the
	/// already-updated entity - the same one the widget-updated notification carries.</summary>
	IDisposable SubscribeDataChanged(string widgetId, Action<WidgetEntity> handler);

	/// <summary>Subscribes to every icon invalidation - <see cref="RaiseIconInvalidated" /> is not keyed by
	/// widget, since an icon id has no owning widget, so every open session sees every invalidation and
	/// filters to the icon ids its own config actually references.</summary>
	IDisposable SubscribeIconInvalidated(Action<Guid> handler);

	/// <summary>Subscribes to every change in what <paramref name="widgetId" />'s icon-provider action
	/// currently contributes - raised whenever <c>IWidgetIconService.Resolve</c>'s answer for this widget
	/// changes, whether that is the provider becoming active, its image changing, going blank, or the
	/// provider stopping being authoritative (falling back to the configured icon). Unlike
	/// <see cref="RaiseIconInvalidated" />, this carries no icon id: a provider image has none, and the
	/// subscriber (<c>ActionButtonWidgetSession</c>) re-resolves for itself rather than being handed a
	/// value.</summary>
	IDisposable SubscribeWidgetIconChanged(string widgetId, Action handler);

	/// <summary>Subscribes to every variable value change, of any scope. Not keyed and not gated by the
	/// widget-variable index the live label path uses: that index is built from stored widget data, and the
	/// only subscriber here is a draft preview, whose unsaved label may reference a variable no stored
	/// widget mentions yet. Carries nothing - the subscriber re-renders its own draft for itself.</summary>
	IDisposable SubscribeVariableChanged(Action handler);

	void RaiseStateChanged(WidgetStateUpdatedEvent evt);

	void RaiseLabelChanged(LabelTextUpdatedEvent evt);

	/// <summary>Raised when a widget's stored configuration was saved.</summary>
	/// <returns>Whether an open session took the change on itself. False means nothing did - either the
	/// widget has no session open, or the one it has built its configuration into its tree and cannot
	/// re-read it - and the caller has to rebuild that widget's sessions for the new configuration to
	/// reach a deck at all.</returns>
	bool RaiseDataChanged(WidgetEntity widget);

	/// <summary>Raised the moment <c>IWidgetIconResources.Evict</c> forgets <paramref name="iconId" />'s
	/// cached handle - <see cref="WidgetIconResourceInvalidationHandler" /> raises this beside that call,
	/// so an open session serving the now-stale handle can re-resolve it instead of drawing the icon's old
	/// bytes for the rest of its lifetime (finding 7: <c>ResolveMissingIconsAsync</c> alone only ever
	/// resolves an id it has never seen, never one it already resolved once).</summary>
	void RaiseIconInvalidated(Guid iconId);

	/// <summary>Raised by <c>WidgetIconProviderPollService</c> whenever <paramref name="widgetId" />'s
	/// icon-provider resolution changed since it was last read.</summary>
	void RaiseWidgetIconChanged(Guid widgetId);

	/// <summary>Raised beside <c>VariableValueChangedNotificationHandler</c>'s own work, for every
	/// variable that changed value - see <see cref="SubscribeVariableChanged" /> for why this one is
	/// ungated.</summary>
	void RaiseVariableChanged();
}

public sealed class WidgetRenderSignals : IWidgetRenderSignals
{
	private readonly SignalRegistry<WidgetStateUpdatedEvent> _state = new();
	private readonly SignalRegistry<LabelTextUpdatedEvent> _label = new();
	private readonly SignalRegistry<WidgetEntity> _data = new();
	private readonly SignalRegistry<Guid> _iconInvalidated = new();
	private readonly SignalRegistry<Unit> _widgetIconChanged = new();
	private readonly SignalRegistry<Unit> _variableChanged = new();

	// Icon invalidation and variable changes have no widget id to key on, unlike the three above - every
	// subscriber shares this one key and filters for itself.
	private const string _unkeyed = "*";

	public IDisposable SubscribeState(string widgetId, Action<WidgetStateUpdatedEvent> handler)
		=> _state.Subscribe(widgetId, handler);

	public IDisposable SubscribeLabel(string widgetId, Action<LabelTextUpdatedEvent> handler)
		=> _label.Subscribe(widgetId, handler);

	public IDisposable SubscribeDataChanged(string widgetId, Action<WidgetEntity> handler)
		=> _data.Subscribe(widgetId, handler);

	public IDisposable SubscribeIconInvalidated(Action<Guid> handler)
		=> _iconInvalidated.Subscribe(_unkeyed, handler);

	public IDisposable SubscribeWidgetIconChanged(string widgetId, Action handler)
		=> _widgetIconChanged.Subscribe(widgetId, _ => handler());

	public IDisposable SubscribeVariableChanged(Action handler)
		=> _variableChanged.Subscribe(_unkeyed, _ => handler());

	public void RaiseStateChanged(WidgetStateUpdatedEvent evt) => _ = _state.Raise(evt.WidgetId, evt);

	public void RaiseLabelChanged(LabelTextUpdatedEvent evt) => _ = _label.Raise(evt.WidgetId, evt);

	public bool RaiseDataChanged(WidgetEntity widget) => _data.Raise(widget.Id.ToString(), widget);

	public void RaiseIconInvalidated(Guid iconId) => _ = _iconInvalidated.Raise(_unkeyed, iconId);

	public void RaiseWidgetIconChanged(Guid widgetId) => _ = _widgetIconChanged.Raise(widgetId.ToString(), default);

	public void RaiseVariableChanged() => _ = _variableChanged.Raise(_unkeyed, default);

	/// <summary>A signal that carries no data of its own - the subscriber always re-resolves for itself.</summary>
	private readonly record struct Unit;

	/// <summary>A keyed multicast: every handler subscribed under a key is invoked, in subscription order,
	/// when that key is raised. Raising a key with no subscriber is a no-op - the common case, since most
	/// widgets never have an open session.</summary>
	private sealed class SignalRegistry<T>
	{
		private readonly object _lock = new();
		private readonly Dictionary<string, List<Action<T>>> _byKey = new(StringComparer.Ordinal);

		public IDisposable Subscribe(string key, Action<T> handler)
		{
			lock (_lock)
			{
				(_byKey.TryGetValue(key, out var list) ? list : _byKey[key] = []).Add(handler);
			}

			return new Subscription(this, key, handler);
		}

		/// <returns>Whether the key had a subscriber - the caller's only way to tell a change nobody is
		/// listening for from one that has been taken care of.</returns>
		public bool Raise(string key, T value)
		{
			Action<T>[] handlers;

			lock (_lock)
			{
				if (!_byKey.TryGetValue(key, out var list))
				{
					return false;
				}

				handlers = list.ToArray();
			}

			foreach (var handler in handlers)
			{
				handler(value);
			}

			return handlers.Length > 0;
		}

		private void Remove(string key, Action<T> handler)
		{
			lock (_lock)
			{
				if (!_byKey.TryGetValue(key, out var list))
				{
					return;
				}

				list.Remove(handler);

				if (list.Count == 0)
				{
					_byKey.Remove(key);
				}
			}
		}

		private sealed class Subscription : IDisposable
		{
			private SignalRegistry<T>? _owner;
			private readonly string _key;
			private readonly Action<T> _handler;

			public Subscription(SignalRegistry<T> owner, string key, Action<T> handler)
			{
				_owner = owner;
				_key = key;
				_handler = handler;
			}

			public void Dispose()
			{
				var owner = Interlocked.Exchange(ref _owner, null);
				owner?.Remove(_key, _handler);
			}
		}
	}
}
