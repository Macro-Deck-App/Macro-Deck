namespace MacroDeckHost.Application.Network.Tls;

public interface IHostNameProvider
{
	string MachineName { get; }

	IReadOnlyList<string> GetHostNames();
}
