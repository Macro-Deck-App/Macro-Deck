using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;

/// <summary>
/// A handler for an arbitrary capability kind. Doubles as the proof that adding a kind needs nothing
/// but a handler - it is a second kind the transport has never heard of, driven end to end.
/// </summary>
internal sealed class TestCapabilityHandler(
	string kind,
	Func<CapabilityInvocation, CancellationToken, Task<CapabilityInvocationResult>>? invoke = null)
	: ICapabilityHandler
{
	public string Kind { get; } = kind;

	public IReadOnlyList<string> LocalIds { get; init; } = ["one"];

	public int Invocations { get; private set; }

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=>
		[
			.. LocalIds.Select(localId => new DeclaredCapability
			{
				Kind = Kind,
				LocalId = localId,
				VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
			})
		];

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		Invocations++;
		return invoke?.Invoke(invocation, cancellationToken) ?? Task.FromResult(CapabilityInvocationResult.Ok());
	}
}
