using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.InputProbe;

internal sealed class StdoutLogSink : ILogEventSink
{
	public void Emit(LogEvent logEvent)
	{
		ProbeOutput.Write(new ProbeEvent
		{
			Type = "log",
			Level = logEvent.Level.ToString(),
			Message = logEvent.RenderMessage()
		});
	}
}
