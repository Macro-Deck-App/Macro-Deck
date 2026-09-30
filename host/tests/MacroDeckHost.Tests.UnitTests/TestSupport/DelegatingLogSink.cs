using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class DelegatingLogSink(Action<LogEvent> emit) : ILogEventSink
{
	public void Emit(LogEvent logEvent) => emit(logEvent);
}
