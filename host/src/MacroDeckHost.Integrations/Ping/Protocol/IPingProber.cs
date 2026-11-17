using MacroDeckHost.Integrations.Ping.State;

namespace MacroDeckHost.Integrations.Ping.Protocol;

internal interface IPingProber
{
	Task<PingProbeResult> ProbeAsync(string host, TimeSpan timeout, CancellationToken cancellationToken);
}

internal sealed record PingProbeResult(PingProblem Problem, long? RoundtripMilliseconds = null)
{
	public bool Succeeded => Problem == PingProblem.None;

	public static PingProbeResult Reply(long roundtripMilliseconds)
	{
		return new PingProbeResult(PingProblem.None, roundtripMilliseconds);
	}
}
