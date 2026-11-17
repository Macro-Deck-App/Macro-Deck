using System.Net;
using System.Net.Sockets;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.WakeOnLan.Protocol;

namespace MacroDeckHost.Tests.UnitTests.WakeOnLan;

internal sealed class ExistingDeviceContext(string title) : IConfigFlowEntryContext
{
	public string? EntryTitle => title;

	public IOAuthSession OAuth => throw new NotSupportedException("This flow does not use OAuth.");
}

internal sealed class RecordingWakeOnLanSender : IWakeOnLanSender
{
	public List<(byte[] Packet, IPEndPoint Target)> Sent { get; } = [];

	public int FailOnSend { get; init; }

	public SocketError Failure { get; init; } = SocketError.HostUnreachable;

	public Task SendAsync(byte[] packet, IPEndPoint target, CancellationToken cancellationToken)
	{
		Sent.Add((packet, target));
		if (Sent.Count == FailOnSend)
		{
			throw new SocketException((int)Failure);
		}

		return Task.CompletedTask;
	}
}
