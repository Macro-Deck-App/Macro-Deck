using System.Collections.Concurrent;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Rendering;

public sealed class LabelRenderFailureLog
{
	internal const int MaxTrackedLabels = 1024;

	private readonly ConcurrentDictionary<(Guid WidgetId, string State), string> _lastLogged = new();
	private readonly ILogger _logger;

	public LabelRenderFailureLog(ILogger logger)
	{
		_logger = logger;
	}

	public void Failed(Guid widgetId, string state, string template, Exception exception)
	{
		if (!ShouldLog((widgetId, state), template + "\n" + exception.Message))
		{
			return;
		}

		_logger.Warning(exception,
			"Label of widget {WidgetId} (state {State}) failed to render and shows a placeholder: {Error}",
			widgetId,
			state,
			exception.Message);
	}

	public void Succeeded(Guid widgetId, string state)
	{
		_lastLogged.TryRemove((widgetId, state), out _);
	}

	public void PreviewFailed(Exception exception)
	{
		_logger.Debug(exception, "Label preview failed to render: {Error}", exception.Message);
	}

	private bool ShouldLog((Guid WidgetId, string State) key, string signature)
	{
		if (_lastLogged.Count >= MaxTrackedLabels)
		{
			_lastLogged.Clear();
		}

		while (true)
		{
			if (_lastLogged.TryGetValue(key, out var previous))
			{
				if (string.Equals(previous, signature, StringComparison.Ordinal))
				{
					return false;
				}

				if (_lastLogged.TryUpdate(key, signature, previous))
				{
					return true;
				}
			}
			else if (_lastLogged.TryAdd(key, signature))
			{
				return true;
			}
		}
	}
}
