namespace MacroDeckHost.Application.Network.Http;

public interface IHttpUserAgentSource
{
	string Current { get; }
}

public sealed class HttpUserAgentState : IHttpUserAgentSource
{
	private volatile string? _custom;

	public string Current => _custom ?? HttpUserAgent.Default;

	public void Apply(string? custom) => _custom = custom;
}
