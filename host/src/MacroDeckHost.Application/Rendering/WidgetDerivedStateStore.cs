using System.Collections.Concurrent;

namespace MacroDeckHost.Application.Rendering;

public sealed class WidgetDerivedStateStore
{
	private readonly ConcurrentDictionary<Guid, string> _states = new();

	public string? GetAndSet(Guid widgetId, string value)
	{
		var previous = _states.TryGetValue(widgetId, out var existing) ? existing : null;
		_states[widgetId] = value;
		return previous;
	}

	public string? TryGet(Guid widgetId) => _states.TryGetValue(widgetId, out var value) ? value : null;

	public void Remove(Guid widgetId) => _states.TryRemove(widgetId, out _);
}
