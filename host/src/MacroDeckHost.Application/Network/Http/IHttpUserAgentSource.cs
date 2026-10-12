using MacroDeck.Sdk.Http;

namespace MacroDeckHost.Application.Network.Http;

public interface IHttpUserAgentSource
{
	string Current { get; }
}

public sealed class HttpUserAgentState : IHttpUserAgentSource
{
	private volatile string? _custom;

	public event Action? Changed;

	public string Current => _custom ?? HttpUserAgent.Default;

	public void Apply(string? custom)
	{
		var previous = Interlocked.Exchange(ref _custom, custom);
		if (!string.Equals(previous, custom, StringComparison.Ordinal))
		{
			Changed?.Invoke();
		}
	}
}

public sealed class HostHttp(IHttpUserAgentSource source) : IHostHttp
{
	public string UserAgent => source.Current;
}
