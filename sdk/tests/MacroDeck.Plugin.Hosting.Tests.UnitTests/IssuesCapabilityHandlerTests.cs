using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Issues;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Issues;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Issues;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class IssuesCapabilityHandlerTests
{
	private static ServiceProvider _services = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp() => _services = new ServiceCollection().BuildServiceProvider();

	[OneTimeTearDown]
	public static void OneTimeTearDown() => _services.Dispose();

	private static CapabilityInvocation Invocation(string localId, string operation, object? arguments = null)
		=> new()
		{
			Kind = CapabilityKinds.Issues,
			LocalId = localId,
			Operation = operation,
			Arguments = arguments is null
				? null
				: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "correlation",
			Services = _services
		};

	private static IntegrationIssue NotConnected()
		=> new() { Id = "not-connected", Title = "Not connected", Severity = IntegrationIssueSeverity.Error };

	[Test]
	public void A_provider_declares_exactly_one_provider_local_id()
	{
		var integration = new TestIssueIntegration(() => [NotConnected()]);
		var handler = new IssuesCapabilityHandler([integration]);

		var declared = handler.DeclareCapabilities();

		Assert.Multiple(() =>
		{
			Assert.That(declared, Has.Count.EqualTo(1));
			Assert.That(declared[0].LocalId, Is.EqualTo(ProviderCapabilityId.LocalId));
			Assert.That(declared[0].Kind, Is.EqualTo(CapabilityKinds.Issues));
		});
	}

	[Test]
	public void No_provider_declares_nothing()
	{
		var handler = new IssuesCapabilityHandler([]);

		Assert.That(handler.DeclareCapabilities(), Is.Empty);
	}

	[Test]
	public async Task Describe_succeeds_with_no_payload()
	{
		var integration = new TestIssueIntegration(() => []);
		var handler = new IssuesCapabilityHandler([integration]);

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId, "describe"),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
	}

	[Test]
	public async Task List_reports_every_issue_across_every_provider()
	{
		var first = new TestIssueIntegration(() => [NotConnected()]);
		var second = new TestIssueIntegration(() =>
			[new IntegrationIssue { Id = "no-permission", Title = "No permission" }]);
		var handler = new IssuesCapabilityHandler([first, second]);

		var result = await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId, "list"),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var payload = result.Data!.Value.Deserialize<IssueListResult>(PluginProtocolJson.Options);
		Assert.That(payload!.Issues.Select(i => i.Id), Is.EquivalentTo(new[] { "not-connected", "no-permission" }));
	}

	[Test]
	public async Task Resolve_finds_the_owning_provider_and_forwards_the_issue_id()
	{
		var integration = new TestIssueIntegration(() => [NotConnected()],
			issueId => IssueResolution.Ok("Reconnected.", IssueResolutionFollowUp.None));
		var handler = new IssuesCapabilityHandler([integration]);

		var result = await handler.InvokeAsync(
			Invocation(ProviderCapabilityId.LocalId, "resolve", new { issueId = "not-connected" }),
			CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var payload = result.Data!.Value.Deserialize<IssueResolveResult>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(payload!.Success, Is.True);
			Assert.That(payload.Message?.Literal, Is.EqualTo("Reconnected."));
			Assert.That(integration.LastResolvedIssueId, Is.EqualTo("not-connected"));
		});
	}

	[Test]
	public async Task Resolve_of_an_issue_no_provider_currently_reports_is_unavailable()
	{
		var integration = new TestIssueIntegration(() => []);
		var handler = new IssuesCapabilityHandler([integration]);

		var result = await handler.InvokeAsync(
			Invocation(ProviderCapabilityId.LocalId, "resolve", new { issueId = "gone" }),
			CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task An_unknown_local_id_is_unavailable_and_an_unknown_operation_is_unsupported()
	{
		var integration = new TestIssueIntegration(() => [NotConnected()]);
		var handler = new IssuesCapabilityHandler([integration]);

		var unknownLocalId = await handler.InvokeAsync(Invocation("nope", "list"), CancellationToken.None);
		var unknownOperation =
			await handler.InvokeAsync(Invocation(ProviderCapabilityId.LocalId, "rewind"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(unknownLocalId.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(unknownOperation.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}
}
