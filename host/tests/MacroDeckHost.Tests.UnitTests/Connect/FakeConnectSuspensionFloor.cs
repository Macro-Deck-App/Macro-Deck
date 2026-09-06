using MacroDeckHost.Application.Connect;

namespace MacroDeckHost.Tests.UnitTests.Connect;

internal sealed class FakeConnectSuspensionFloor : IConnectSuspensionFloor
{
	public DateTimeOffset? NotBefore { get; set; }

	public Task<DateTimeOffset?> Read(CancellationToken cancellationToken = default)
		=> Task.FromResult(NotBefore);

	public Task Write(DateTimeOffset notBefore, CancellationToken cancellationToken = default)
	{
		NotBefore = notBefore;
		return Task.CompletedTask;
	}
}
