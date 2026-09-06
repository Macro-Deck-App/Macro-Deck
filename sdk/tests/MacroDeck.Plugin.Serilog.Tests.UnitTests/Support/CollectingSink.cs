using Serilog.Core;
using Serilog.Events;

namespace MacroDeck.Plugin.Serilog.Tests.UnitTests.Support;

/// <summary>A second, independent sink in the same pipeline - what it received is the ground truth for
/// "did the author's own log calls produce exactly this many events", uncontaminated by anything
/// <c>MacroDeckLogSink</c>/<c>MacroDeckLogShipper</c> might otherwise have injected into the pipeline.</summary>
internal sealed class CollectingSink : ILogEventSink
{
	private readonly List<LogEvent> _events = [];
	private readonly Lock _gate = new();

	public IReadOnlyList<LogEvent> Events
	{
		get
		{
			lock (_gate)
			{
				return [.. _events];
			}
		}
	}

	public void Emit(LogEvent logEvent)
	{
		lock (_gate)
		{
			_events.Add(logEvent);
		}
	}
}
