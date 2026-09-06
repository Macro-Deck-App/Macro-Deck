using MacroDeck.Plugin.Hosting.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class VariablesContractTests : CapabilityContractFixture
{
	private static DeclaredCapability Variable(string localId)
		=> new()
		{
			Kind = CapabilityKinds.Variables, LocalId = localId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	[Test]
	public async Task Declare_registers_an_adapter_that_passes_the_capability_validator()
	{
		var variable = VariableDefinition.Eager("cpu_temp", VariableType.Numeric) with { Id = "cpu-temp" };
		var integration = await ConnectAsync([Handler(new TestVariableIntegration([variable]))],
			[Variable("cpu-temp")],
			[CapabilityKinds.Variables]);

		Assert.Multiple(() =>
		{
			Assert.That(IntegrationCapabilityValidator.Validate(integration), Is.Empty);
			Assert.That(((IVariableProvider)integration).Variables.Select(v => v.Name),
				Does.Contain("cpu_temp"));
		});
	}

	[Test]
	public async Task Every_operation_round_trips_to_the_sdk_type_the_host_contract_requires()
	{
		var variable = VariableDefinition.Eager("cpu_temp", VariableType.Numeric, decimalPlaces: 1)
			with
			{
				Id = "cpu-temp"
			};
		var integration = await ConnectAsync([
				Handler(new TestVariableIntegration([variable],
					dependsOnConfiguration: true,
					read: (_, _) => ValueTask.FromResult(VariableReading.Of(42.5))))
			],
			[Variable("cpu-temp")],
			[CapabilityKinds.Variables]);

		var provider = (IVariableProvider)integration;

		Assert.Multiple(() =>
		{
			Assert.That(provider.DeclaredVariables.Select(v => v.Name), Is.EqualTo(new[] { "cpu_temp" }));
			Assert.That(provider.Variables.Single().DecimalPlaces, Is.EqualTo(1));
			Assert.That(provider.VariablesDependOnConfiguration, Is.True);
		});

		var value = (await provider.ReadAsync("cpu-temp", CancellationToken.None)).Value;
		Assert.That(value, Is.EqualTo(42.5));
	}

	[Test]
	public async Task A_timeout_degrades_to_a_null_value_never_an_exception()
	{
		var variable = VariableDefinition.Eager("cpu_temp", VariableType.Numeric) with { Id = "cpu-temp" };
		var integration = await ConnectAsync([Handler(new TestVariableIntegration([variable], read: NeverReplies))],
			[Variable("cpu-temp")],
			[CapabilityKinds.Variables]);

		var provider = (IVariableProvider)integration;
		var getTask = provider.ReadAsync("cpu-temp", CancellationToken.None).AsTask();

		Time.Advance(ProtocolTimeouts.CapabilityInvoke);
		var reading = await getTask;

		Assert.That(reading.Value, Is.Null);
	}

	[Test]
	public async Task A_dropped_connection_degrades_to_a_null_value_never_an_exception()
	{
		var variable = VariableDefinition.Eager("cpu_temp", VariableType.Numeric) with { Id = "cpu-temp" };
		var integration = await ConnectAsync([Handler(new TestVariableIntegration([variable]))],
			[Variable("cpu-temp")],
			[CapabilityKinds.Variables]);

		Disconnect();

		var provider = (IVariableProvider)integration;
		var value = (await provider.ReadAsync("cpu-temp", CancellationToken.None)).Value;

		Assert.That(value, Is.Null);
	}

	[Test]
	public async Task Caller_cancellation_puts_capability_cancel_on_the_wire()
	{
		var stillRunning = new TaskCompletionSource<VariableReading>();
		var variable = VariableDefinition.Eager("cpu_temp", VariableType.Numeric) with { Id = "cpu-temp" };
		var integration = await ConnectAsync([
				Handler(new TestVariableIntegration([variable],
					read: (_, _) => new ValueTask<VariableReading>(stillRunning.Task)))
			],
			[Variable("cpu-temp")],
			[CapabilityKinds.Variables]);

		using var cts = new CancellationTokenSource();
		var provider = (IVariableProvider)integration;
		var getTask = provider.ReadAsync("cpu-temp", cts.Token).AsTask();

		await cts.CancelAsync();

		Assert.CatchAsync<OperationCanceledException>(async () => await getTask);
		Assert.That(Link.SentByHost.Any(envelope => envelope.Type == MessageTypes.CapabilityCancel), Is.True);

		stillRunning.TrySetResult(VariableReading.Unavailable);
	}

	[Test]
	public async Task A_throwing_handler_yields_a_redacted_internal_error()
	{
		var variable = VariableDefinition.Eager("cpu_temp", VariableType.Numeric) with { Id = "cpu-temp" };
		await ConnectAsync([
				Handler(new TestVariableIntegration([variable],
					read: (_, _) => throw new InvalidOperationException("boom: token=abc123")))
			],
			[Variable("cpu-temp")],
			[CapabilityKinds.Variables]);

		var exception = Assert.CatchAsync<RemoteCapabilityException>(async () =>
			await InvokeRawAsync(CapabilityKinds.Variables, "cpu-temp", CapabilityOperations.Variables.Get));

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
		var variable = VariableDefinition.Eager("cpu_temp", VariableType.Numeric) with { Id = "cpu-temp" };
		await ConnectAsync([Handler(new TestVariableIntegration([variable]))],
			[Variable("cpu-temp")],
			[CapabilityKinds.Variables]);

		var unknownLocalId = Assert.CatchAsync<RemoteCapabilityException>(async () =>
			await InvokeRawAsync(CapabilityKinds.Variables, "nope", CapabilityOperations.Variables.Get));
		var unknownOperation = Assert.CatchAsync<RemoteCapabilityException>(async () =>
			await InvokeRawAsync(CapabilityKinds.Variables, "cpu-temp", "rewind"));

		Assert.Multiple(() =>
		{
			Assert.That(unknownLocalId!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(unknownOperation!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}

	private static VariablesCapabilityHandler Handler(IPluginIntegration provider)
		=> new([provider],
			TestMetadata.Default,
			new VariableSubscriptions(Serilog.Log.Logger),
			Serilog.Core.Logger.None);

	private static async ValueTask<VariableReading> NeverReplies(string localId, CancellationToken cancellationToken)
	{
		await Task.Delay(Timeout.Infinite, cancellationToken);
		return VariableReading.Unavailable;
	}
}
