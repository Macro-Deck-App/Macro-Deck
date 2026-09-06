using MacroDeck.Plugin.Hosting.Capabilities.Issues;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk.Issues;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class IssuesContractTests : CapabilityContractFixture
{
	private static DeclaredCapability Provider()
		=> new()
		{
			Kind = CapabilityKinds.Issues,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	private static IntegrationIssue NotConnected()
		=> new() { Id = "not-connected", Title = "Not connected", Severity = IntegrationIssueSeverity.Error };

	private static Task<IReadOnlyList<IntegrationIssue>> OneIssue(CancellationToken cancellationToken = default)
		=> Task.FromResult<IReadOnlyList<IntegrationIssue>>([NotConnected()]);

	[Test]
	public async Task Declare_registers_an_adapter_that_passes_the_capability_validator()
	{
		var integration = await ConnectAsync([new IssuesCapabilityHandler([new TestIssueIntegration(OneIssue)])],
			[Provider()],
			[CapabilityKinds.Issues]);

		Assert.That(IntegrationCapabilityValidator.Validate(integration), Is.Empty);
	}

	[Test]
	public async Task Every_operation_round_trips_to_the_sdk_type_the_host_contract_requires()
	{
		var integration = await ConnectAsync([
				new IssuesCapabilityHandler([
					new TestIssueIntegration(OneIssue, (_, _) => Task.FromResult(IssueResolution.Ok("Reconnected.")))
				])
			],
			[Provider()],
			[CapabilityKinds.Issues]);

		var provider = (IIntegrationIssueProvider)integration;

		var issues = await provider.GetIssuesAsync(CancellationToken.None);
		Assert.That(issues.Single().Id, Is.EqualTo("not-connected"));

		var resolution = await provider.ResolveIssueAsync("not-connected", CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(resolution.Success, Is.True);
			Assert.That(resolution.Message.Literal, Is.EqualTo("Reconnected."));
		});
	}

	[Test]
	public async Task A_timeout_degrades_to_empty_and_a_failed_resolution_never_an_exception()
	{
		var integration = await ConnectAsync([
				new IssuesCapabilityHandler([
					new TestIssueIntegration(NeverRepliesWithIssues, NeverRepliesWithResolution)
				])
			],
			[Provider()],
			[CapabilityKinds.Issues]);

		var provider = (IIntegrationIssueProvider)integration;
		var listTask = provider.GetIssuesAsync(CancellationToken.None);

		Time.Advance(ProtocolTimeouts.CapabilityInvoke);
		var issues = await listTask;

		Assert.That(issues, Is.Empty);

		var resolveTask = provider.ResolveIssueAsync("not-connected", CancellationToken.None);
		Time.Advance(ProtocolTimeouts.CapabilityInvoke);
		var resolution = await resolveTask;

		Assert.That(resolution.Success, Is.False);
	}

	[Test]
	public async Task A_dropped_connection_degrades_to_empty_and_a_failed_resolution_never_an_exception()
	{
		var integration = await ConnectAsync([new IssuesCapabilityHandler([new TestIssueIntegration(OneIssue)])],
			[Provider()],
			[CapabilityKinds.Issues]);

		Disconnect();

		var provider = (IIntegrationIssueProvider)integration;
		var issues = await provider.GetIssuesAsync(CancellationToken.None);
		var resolution = await provider.ResolveIssueAsync("not-connected", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(issues, Is.Empty);
			Assert.That(resolution.Success, Is.False);
		});
	}

	[Test]
	public async Task Caller_cancellation_puts_capability_cancel_on_the_wire()
	{
		var stillRunning = new TaskCompletionSource<IReadOnlyList<IntegrationIssue>>();
		var integration = await ConnectAsync(
			[new IssuesCapabilityHandler([new TestIssueIntegration(_ => stillRunning.Task)])],
			[Provider()],
			[CapabilityKinds.Issues]);

		using var cts = new CancellationTokenSource();
		var provider = (IIntegrationIssueProvider)integration;
		var listTask = provider.GetIssuesAsync(cts.Token);

		await cts.CancelAsync();

		Assert.CatchAsync<OperationCanceledException>(async () => await listTask);
		Assert.That(Link.SentByHost.Any(envelope => envelope.Type == MessageTypes.CapabilityCancel), Is.True);

		stillRunning.TrySetResult([]);
	}

	[Test]
	public async Task A_throwing_handler_yields_a_redacted_internal_error()
	{
		await ConnectAsync([
				new IssuesCapabilityHandler([
					new TestIssueIntegration(_ => throw new InvalidOperationException("boom: token=abc123"))
				])
			],
			[Provider()],
			[CapabilityKinds.Issues]);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () =>
			await InvokeRawAsync(CapabilityKinds.Issues,
				ProviderCapabilityId.LocalId,
				CapabilityOperations.Issues.List));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.InternalError));
			Assert.That(exception.Message, Does.Not.Contain("boom"));
			Assert.That(exception.Message, Does.Not.Contain("token=abc123"));
		});
	}

	[Test]
	public async Task An_unknown_local_id_is_unavailable_and_an_unknown_operation_is_unsupported()
	{
		await ConnectAsync([new IssuesCapabilityHandler([new TestIssueIntegration(OneIssue)])],
			[Provider()],
			[CapabilityKinds.Issues]);

		var unknownLocalId = Assert.CatchAsync<RemoteCapabilityException>(async () =>
			await InvokeRawAsync(CapabilityKinds.Issues, "nope", CapabilityOperations.Issues.List));
		var unknownOperation = Assert.CatchAsync<RemoteCapabilityException>(async () =>
			await InvokeRawAsync(CapabilityKinds.Issues, ProviderCapabilityId.LocalId, "rewind"));

		Assert.Multiple(() =>
		{
			Assert.That(unknownLocalId!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(unknownOperation!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}

	private static async Task<IReadOnlyList<IntegrationIssue>> NeverRepliesWithIssues(
		CancellationToken cancellationToken)
	{
		await Task.Delay(Timeout.Infinite, cancellationToken);
		return [];
	}

	private static async Task<IssueResolution> NeverRepliesWithResolution(string issueId,
		CancellationToken cancellationToken)
	{
		await Task.Delay(Timeout.Infinite, cancellationToken);
		return IssueResolution.Failed();
	}
}
