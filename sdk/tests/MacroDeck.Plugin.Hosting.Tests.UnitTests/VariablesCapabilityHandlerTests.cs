using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Variables;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Variables;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>The eager half of <see cref="VariablesCapabilityHandler" /> - declaration, read and write.
/// The catalog half is covered by <see cref="VariableCatalogCapabilityHandlerTests" />.</summary>
[TestFixture]
public class VariablesCapabilityHandlerTests
{
	private static ServiceProvider _services = null!;

	[OneTimeSetUp]
	public static void OneTimeSetUp() => _services = new ServiceCollection().BuildServiceProvider();

	[OneTimeTearDown]
	public static void OneTimeTearDown() => _services.Dispose();

	private static CapabilityInvocation Invocation(string localId, string operation, object? arguments = null)
		=> new()
		{
			Kind = CapabilityKinds.Variables,
			LocalId = localId,
			Operation = operation,
			Arguments = arguments is null
				? null
				: JsonSerializer.SerializeToElement(arguments, PluginProtocolJson.Options),
			CorrelationId = "correlation",
			Services = _services
		};

	private static VariablesCapabilityHandler Handler(params IPluginIntegration[] integrations)
		=> new(integrations,
			TestMetadata.Default,
			new VariableSubscriptions(Serilog.Log.Logger),
			Serilog.Core.Logger.None);

	private static VariableDefinition Numeric(string name, string? id = null)
		=> new()
		{
			Id = id, Name = name, Type = VariableType.Numeric, Materialization = VariableMaterialization.Eager
		};

	[Test]
	public void Every_declared_variable_is_declared_by_its_definition_id()
	{
		var integration = new TestVariableIntegration([
			Numeric("cpu_temp", "cpu-temp"),
			Numeric("gpu_temp")
		]);

		var handler = Handler(integration);
		var declared = handler.DeclareCapabilities();

		Assert.Multiple(() =>
		{
			Assert.That(declared.Select(c => c.LocalId), Is.EquivalentTo(new[] { "cpu-temp", "gpu-temp" }));
			Assert.That(declared, Has.All.Property(nameof(DeclaredCapability.Kind)).EqualTo(CapabilityKinds.Variables));
		});
	}

	[Test]
	public void A_template_declared_variable_with_no_definition_id_is_not_declared_as_its_own_capability()
	{
		var integration = new TestVariableIntegration(variables: [],
			declaredVariables:
			[
				new VariableDefinition
				{
					Name = VariableNameTemplate.Placeholder("account"),
					Type = VariableType.Text,
					Materialization = VariableMaterialization.Eager
				}
			]);

		Assert.That(Handler(integration).DeclareCapabilities(), Is.Empty);
	}

	/// <summary>An on-demand definition is reached through the catalog, never declared: declaring one
	/// would give the same variable both a declared local id and a resource id.</summary>
	[Test]
	public void An_on_demand_definition_is_not_declared()
	{
		var integration = new TestVariableIntegration([VariableDefinition.OnDemand("entity/a", VariableType.Text)]);

		Assert.That(Handler(integration).DeclareCapabilities(), Is.Empty);
	}

	/// <summary>
	/// A provider that declares more eager variables than one provider may keeps the first N in
	/// declaration order rather than losing its whole variable surface.
	/// </summary>
	[Test]
	public void More_eager_variables_than_the_bound_are_truncated_not_rejected()
	{
		var integration = new TestVariableIntegration([
			.. Enumerable.Range(0, VariableLimits.MaxEagerVariablesPerProvider + 10)
				.Select(index => Numeric($"var_{index}"))
		]);

		var declared = Handler(integration).DeclareCapabilities();

		Assert.Multiple(() =>
		{
			Assert.That(declared, Has.Count.EqualTo(VariableLimits.MaxEagerVariablesPerProvider));
			Assert.That(declared[0].LocalId, Is.EqualTo("var-0"));
		});
	}

	[Test]
	public async Task Describe_lists_both_declared_and_eager_variables()
	{
		var integration = new TestVariableIntegration(variables: [Numeric("cpu_temp", "cpu-temp")],
			dependsOnConfiguration: true);

		var handler = Handler(integration);
		var result = await handler.InvokeAsync(Invocation("*", "describe"), CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var payload = result.Data!.Value.Deserialize<VariableCatalogPayload>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(payload!.DeclaredVariables, Has.Count.EqualTo(1));
			Assert.That(payload.Variables, Has.Count.EqualTo(1));
			Assert.That(payload.VariablesDependOnConfiguration, Is.True);
		});
	}

	/// <summary>The attributes and the write capability are what a consumer decides a range, a format and
	/// whether to offer a control from, so they have to survive the describe hop rather than being
	/// reconstructed per consumer.</summary>
	[Test]
	public async Task Describe_carries_the_attributes_and_the_write_capability()
	{
		var integration = new TestVariableIntegration([
			new VariableDefinition
			{
				Id = "gain",
				Name = "gain",
				Type = VariableType.Numeric,
				Materialization = VariableMaterialization.Eager,
				Unit = "%",
				SemanticKind = VariableSemanticKinds.Percentage,
				DecimalPlaces = 1,
				Write = new VariableWriteCapability { CommitOnRelease = true }
			}
		]);

		var result = await Handler(integration).InvokeAsync(Invocation("*", "describe"), CancellationToken.None);
		var payload = result.Data!.Value.Deserialize<VariableCatalogPayload>(PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(payload!.Variables[0].Unit, Is.EqualTo("%"));
			Assert.That(payload.Variables[0].SemanticKind, Is.EqualTo(VariableSemanticKinds.Percentage));
			Assert.That(payload.Variables[0].DecimalPlaces, Is.EqualTo(1));
			Assert.That(payload.Variables[0].Write?.CommitOnRelease, Is.True);
		});
	}

	[Test]
	public async Task Get_resolves_by_definition_id_and_reads_by_the_same_id()
	{
		var integration = new TestVariableIntegration(variables: [Numeric("cpu_temp", "cpu-temp")],
			read: _ => VariableReading.Of(42.5));

		var handler = Handler(integration);
		var result = await handler.InvokeAsync(Invocation("cpu-temp", "get"), CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var reading = result.Data!.Value.Deserialize<VariableReadingDto>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(reading!.Value.Kind, Is.EqualTo("number"));
			Assert.That(reading.Value.Number, Is.EqualTo(42.5));
			Assert.That(integration.LastRequestedId, Is.EqualTo("cpu-temp"));
		});
	}

	/// <summary>The volatile attributes travel with the value, not with the declaration - a media
	/// player's seek range is its current track's length - so a read has to carry them.</summary>
	[Test]
	public async Task Get_carries_the_volatile_attributes_of_the_reading()
	{
		var integration = new TestVariableIntegration(variables: [Numeric("position", "position")],
			read: _ => VariableReading.Of(187d, 0, 245, 1));

		var result = await Handler(integration).InvokeAsync(Invocation("position", "get"), CancellationToken.None);
		var reading = result.Data!.Value.Deserialize<VariableReadingDto>(PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(reading!.Min, Is.EqualTo(0));
			Assert.That(reading.Max, Is.EqualTo(245));
			Assert.That(reading.Step, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Get_of_a_null_value_answers_unavailable_not_a_failure()
	{
		var integration = new TestVariableIntegration(variables: [Numeric("cpu_temp", "cpu-temp")],
			read: _ => VariableReading.Of(null));

		var handler = Handler(integration);
		var result = await handler.InvokeAsync(Invocation("cpu-temp", "get"), CancellationToken.None);

		Assert.That(result.IsFailure, Is.False);
		var reading = result.Data!.Value.Deserialize<VariableReadingDto>(PluginProtocolJson.Options);
		Assert.That(reading!.Value.Kind, Is.EqualTo("unavailable"));
	}

	[Test]
	public async Task An_unknown_local_id_is_unavailable()
	{
		var handler = Handler(new TestVariableIntegration([Numeric("cpu_temp")]));

		var result = await handler.InvokeAsync(Invocation("nope", "get"), CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	[Test]
	public async Task An_unknown_operation_is_unsupported()
	{
		var handler = Handler(new TestVariableIntegration([Numeric("cpu_temp")]));

		var result = await handler.InvokeAsync(Invocation("cpu-temp", "rewind"), CancellationToken.None);

		Assert.That(result.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
	}

	[Test]
	public async Task Set_forwards_the_value_to_the_owning_provider()
	{
		var integration = new TestVariableIntegration([
			new VariableDefinition
			{
				Id = "gain",
				Name = "gain",
				Type = VariableType.Numeric,
				Materialization = VariableMaterialization.Eager,
				Write = new VariableWriteCapability()
			}
		]);

		var result = await Handler(integration).InvokeAsync(Invocation("gain",
				CapabilityOperations.Variables.Set,
				new VariableSetArguments { Value = new VariableValueDto { Kind = "number", Number = 62.5 } }),
			CancellationToken.None);

		var written = result.Data!.Value.Deserialize<VariableSetResult>(PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(written!.Status, Is.EqualTo(nameof(VariableWriteStatus.Applied)));
			Assert.That(integration.LastWrittenValue, Is.EqualTo(62.5));
		});
	}

	/// <summary>
	/// The declaration is the gate, not the provider: a definition carrying no write capability is
	/// refused by the handler, so a provider that never implemented a write is never asked to.
	/// </summary>
	[Test]
	public async Task Set_of_a_variable_that_declares_no_write_capability_is_refused_without_reaching_the_provider()
	{
		var integration = new TestVariableIntegration([Numeric("cpu_temp", "cpu-temp")]);

		var result = await Handler(integration).InvokeAsync(Invocation("cpu-temp",
				CapabilityOperations.Variables.Set,
				new VariableSetArguments { Value = new VariableValueDto { Kind = "number", Number = 1 } }),
			CancellationToken.None);

		var written = result.Data!.Value.Deserialize<VariableSetResult>(PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(written!.Status, Is.EqualTo(nameof(VariableWriteStatus.NotWritable)));
			Assert.That(integration.LastWrittenValue, Is.Null);
		});
	}

	[Test]
	public async Task Set_of_an_unknown_local_id_answers_not_found()
	{
		var handler = Handler(new TestVariableIntegration([Numeric("cpu_temp", "cpu-temp")]));

		var result = await handler.InvokeAsync(Invocation("nope",
				CapabilityOperations.Variables.Set,
				new VariableSetArguments { Value = new VariableValueDto { Kind = "number", Number = 1 } }),
			CancellationToken.None);

		var written = result.Data!.Value.Deserialize<VariableSetResult>(PluginProtocolJson.Options);

		Assert.That(written!.Status, Is.EqualTo(nameof(VariableWriteStatus.NotFound)));
	}
}
