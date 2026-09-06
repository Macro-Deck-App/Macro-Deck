using MacroDeckHost.Application.Ui.Transport;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

/// <summary>A transport that records nothing: a contract test asserts on what crosses the plugin socket,
/// not on what the host pushed to a client.</summary>
internal sealed class NullUiTransport : IUiTransport
{
	public Task Send<T>(T message, CancellationToken cancellationToken = default)
		where T : class
		=> Task.CompletedTask;

	public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
		where T : class
		=> Task.CompletedTask;

	public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
		where T : class
		=> Task.CompletedTask;

	public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;

	public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
		=> Task.CompletedTask;
}
