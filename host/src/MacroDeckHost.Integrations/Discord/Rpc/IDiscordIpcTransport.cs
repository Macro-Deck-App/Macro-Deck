namespace MacroDeckHost.Integrations.Discord.Rpc;

internal interface IDiscordIpcTransport : IDisposable
{
	bool IsConnected { get; }

	string? Endpoint { get; }

	Task ConnectAsync(IReadOnlySet<string> skippedEndpoints, CancellationToken cancellationToken);

	Task WriteFrameAsync(DiscordRpcOpcode opcode, ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);

	Task<DiscordIpcFrame?> ReadFrameAsync(CancellationToken cancellationToken);
}
