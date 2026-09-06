using MacroDeck.Plugin.Protocol.Envelope;

namespace MacroDeckHost.Application.Plugins;

public interface IPluginConnection
{
	string ConnectionId { get; }

	Task Send(ProtocolEnvelope envelope, CancellationToken cancellationToken = default);

	Task Close(int closeCode, string reason, CancellationToken cancellationToken = default);
}
