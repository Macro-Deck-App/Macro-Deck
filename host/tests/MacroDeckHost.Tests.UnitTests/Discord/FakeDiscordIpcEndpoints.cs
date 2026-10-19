using MacroDeckHost.Integrations.Discord.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Discord;

internal sealed class FakeDiscordIpcEndpoints
{
	private readonly Func<FakeDiscordIpcTransport>[] _endpoints;

	public FakeDiscordIpcEndpoints(params Func<FakeDiscordIpcTransport>[] endpoints)
	{
		_endpoints = endpoints;
	}

	public List<FakeDiscordIpcTransport> Opened { get; } = [];

	public IDiscordIpcTransport CreateTransport()
	{
		return new RoutingTransport(this);
	}

	private sealed class RoutingTransport : IDiscordIpcTransport
	{
		private readonly FakeDiscordIpcEndpoints _owner;
		private FakeDiscordIpcTransport? _selected;

		public RoutingTransport(FakeDiscordIpcEndpoints owner)
		{
			_owner = owner;
		}

		public bool IsConnected => _selected?.IsConnected ?? false;

		public string? Endpoint => _selected?.Endpoint;

		public async Task ConnectAsync(IReadOnlySet<string> skippedEndpoints, CancellationToken cancellationToken)
		{
			DiscordIpcDenial? denial = null;
			foreach (var create in _owner._endpoints)
			{
				var endpoint = create();
				try
				{
					await endpoint.ConnectAsync(skippedEndpoints, cancellationToken);
				}
				catch (DiscordIpcUnavailableException ex)
				{
					denial ??= ex.Denial;
					continue;
				}

				_owner.Opened.Add(endpoint);
				_selected = endpoint;
				return;
			}

			throw new DiscordIpcUnavailableException("No usable endpoint.", denial);
		}

		public Task WriteFrameAsync(
			DiscordRpcOpcode opcode,
			ReadOnlyMemory<byte> payload,
			CancellationToken cancellationToken)
		{
			return Selected.WriteFrameAsync(opcode, payload, cancellationToken);
		}

		public Task<DiscordIpcFrame?> ReadFrameAsync(CancellationToken cancellationToken)
		{
			return Selected.ReadFrameAsync(cancellationToken);
		}

		public void Dispose()
		{
			_selected?.Dispose();
		}

		private FakeDiscordIpcTransport Selected
			=> _selected ?? throw new DiscordIpcProtocolException("The transport is not connected.");
	}
}
