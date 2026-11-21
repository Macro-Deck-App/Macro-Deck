using System.Collections.Concurrent;
using MacroDeckHost.Integrations.Ping.Protocol;
using MacroDeckHost.Integrations.Ping.State;

namespace MacroDeckHost.Tests.UnitTests.Ping;

internal sealed class FakePingProber : IPingProber
{
	private readonly ConcurrentDictionary<string, ConcurrentQueue<PingProbeResult>> _scripts =
		new(StringComparer.OrdinalIgnoreCase);

	private readonly ConcurrentDictionary<string, int> _calls = new(StringComparer.OrdinalIgnoreCase);

	public PingProbeResult Fallback { get; set; } = PingProbeResult.Reply(5);

	public void Script(string host, params PingProbeResult[] results)
	{
		var queue = _scripts.GetOrAdd(host, _ => new ConcurrentQueue<PingProbeResult>());
		foreach (var result in results)
		{
			queue.Enqueue(result);
		}
	}

	public int Calls(string host)
	{
		return _calls.GetValueOrDefault(host);
	}

	public Task<PingProbeResult> ProbeAsync(string host, TimeSpan timeout, CancellationToken cancellationToken)
	{
		_calls.AddOrUpdate(host, 1, (_, count) => count + 1);
		return Task.FromResult(_scripts.TryGetValue(host, out var queue) && queue.TryDequeue(out var next)
			? next
			: Fallback);
	}

	public static PingProbeResult Fail(PingProblem problem = PingProblem.TimedOut)
	{
		return new PingProbeResult(problem);
	}
}
