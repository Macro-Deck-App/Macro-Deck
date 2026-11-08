using System.Text.Json;

namespace MacroDeckHost.Tests.InputProbe;

internal static class ProbeOutput
{
	private static readonly Lock _gate = new();

	public static void Write(ProbeEvent probeEvent)
	{
		var line = JsonSerializer.Serialize(probeEvent, ProbeEvent.JsonOptions);
		lock (_gate)
		{
			Console.Out.WriteLine(line);
			Console.Out.Flush();
		}
	}

	public static void Error(string message)
	{
		Write(new ProbeEvent { Type = "error", Message = message });
	}
}
