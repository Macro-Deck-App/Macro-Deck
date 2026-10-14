using System.Text.Json;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Plugin.Testing.Conformance;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeck.Plugin.Testing.Tests.ConformanceTests;

[TestFixture]
internal sealed class CatalogOnlyVariablesSuiteTests : ConformanceFixture
{
	protected override Task<ConformanceSubject> CreateSubjectAsync()
	{
		return Task.FromResult(ConformanceSubject.InProcess(builder
				=> builder.RegisterIntegration(_ => new CatalogOnlyIntegration()),
			new PluginTestManifest("app.macro-deck.catalog-only-test-plugin",
				"Catalog Only Test Plugin",
				"1.0.0")));
	}

	[TestCase("MDC0311", ConformanceOutcome.Passed)]
	[TestCase("MDC0315", ConformanceOutcome.Passed)]
	[TestCase("MDC0403", ConformanceOutcome.Passed)]
	public void The_variables_checks_run_against_the_catalog(string checkId, ConformanceOutcome expected)
	{
		Assert.That(Report.Results.Single(result => result.Id == checkId).Result.Outcome, Is.EqualTo(expected));
	}

	private sealed class CatalogOnlyIntegration : IPluginIntegration, IVariableProvider
	{
		private static readonly VariableDefinition _level = new()
		{
			Id = "device-level",
			Name = "level",
			Type = VariableType.Numeric,
			Materialization = VariableMaterialization.OnDemand
		};

		public IReadOnlyList<IActionDefinition> Actions => [];

		public IReadOnlyList<VariableDefinition> Variables => [];

		public IReadOnlyList<VariableDefinition> DeclaredVariables => [];

		public bool SupportsCatalog => true;

		public string CatalogName => "Devices";

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult(VariableReading.Of(1.0));
		}

		public ValueTask<VariableCatalogPage> DiscoverAsync(
			VariableCatalogQuery query,
			CancellationToken cancellationToken = default)
		{
			return ValueTask.FromResult(new VariableCatalogPage { Items = [_level] });
		}
	}
}

[TestFixture]
internal sealed class UndeclaredVariablesConformanceTests
{
	private static readonly string[] _variablesChecks = ["MDC0311", "MDC0314", "MDC0315", "MDC0403"];

	[Test]
	public async Task A_subject_without_a_variables_provider_skips_every_variables_check()
	{
		var report = await RunAsync(builder => builder.RegisterIntegration(_ => new ActionsOnlyIntegration()));

		Assert.That(report.Results.Select(result => (result.Id, result.Result.Outcome)),
			Is.EquivalentTo(_variablesChecks.Select(id => (id, ConformanceOutcome.Skipped))));
	}

	[Test]
	public async Task A_subject_that_answers_describe_with_capability_unsupported_skips_every_variables_check()
	{
		var report = await RunAsync(builder => builder
			.RegisterIntegration(_ => new ActionsOnlyIntegration())
			.RegisterCapabilityHandler<UnsupportedVariablesHandler>());

		Assert.That(report.Results.Select(result => (result.Id, result.Result.Outcome)),
			Is.EquivalentTo(_variablesChecks.Select(id => (id, ConformanceOutcome.Skipped))));
	}

	[Test]
	public async Task A_malformed_catalog_fails_its_check_even_when_nothing_is_declared()
	{
		var report = await RunAsync(builder => builder
			.RegisterIntegration(_ => new ActionsOnlyIntegration())
			.RegisterCapabilityHandler<MalformedCatalogHandler>());

		Assert.That(report.Results.Single(result => result.Id == "MDC0311").Result.Outcome,
			Is.EqualTo(ConformanceOutcome.Failed));
	}

	[Test]
	public async Task A_subject_that_answers_describe_with_something_else_skips_every_variables_check()
	{
		var report = await RunAsync(builder => builder
			.RegisterIntegration(_ => new ActionsOnlyIntegration())
			.RegisterCapabilityHandler<UnrelatedDescribeHandler>());

		Assert.That(report.Results.Select(result => (result.Id, result.Result.Outcome)),
			Is.EquivalentTo(_variablesChecks.Select(id => (id, ConformanceOutcome.Skipped))));
	}

	[Test]
	public async Task A_subject_whose_describe_carries_null_variable_lists_skips_every_variables_check()
	{
		var report = await RunAsync(builder => builder
			.RegisterIntegration(_ => new ActionsOnlyIntegration())
			.RegisterCapabilityHandler<NullListsDescribeHandler>());

		Assert.That(report.Results.Select(result => (result.Id, result.Result.Outcome)),
			Is.EquivalentTo(_variablesChecks.Select(id => (id, ConformanceOutcome.Skipped))));
	}

	[Test]
	public async Task A_subject_that_declares_variables_and_cannot_describe_them_fails_every_variables_check()
	{
		var report = await RunAsync(builder => builder
			.RegisterIntegration(_ => new ActionsOnlyIntegration())
			.RegisterCapabilityHandler<DeclaredButUnsupportedVariablesHandler>());

		Assert.That(report.Results.Select(result => (result.Id, result.Result.Outcome)),
			Is.EquivalentTo(_variablesChecks.Select(id => (id, ConformanceOutcome.Failed))));
	}

	[Test]
	public async Task An_undeclared_writable_variable_that_refuses_writes_fails_its_check()
	{
		var report = await RunAsync(builder => builder
			.RegisterIntegration(_ => new ActionsOnlyIntegration())
			.RegisterCapabilityHandler<RefusingWritableVariableHandler>());

		Assert.That(report.Results.Single(result => result.Id == "MDC0314").Result.Outcome,
			Is.EqualTo(ConformanceOutcome.Failed));
	}

	private static async Task<ConformanceReport> RunAsync(Func<PluginHostBuilder, PluginHostBuilder> configure)
	{
		var subject = ConformanceSubject.InProcess(builder => configure(builder),
			new PluginTestManifest("app.macro-deck.undeclared-variables-test-plugin",
				"Undeclared Variables Test Plugin",
				"1.0.0"));

		try
		{
			return await new ConformanceRunner(new ConformanceOptions { Ids = _variablesChecks }).RunAsync(subject);
		}
		finally
		{
			await subject.DisposeAsync();
		}
	}

	private sealed class ActionsOnlyIntegration : IPluginIntegration
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}
	}

	private sealed class UnsupportedVariablesHandler : ICapabilityHandler
	{
		public string Kind => CapabilityKinds.Variables;

		public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		{
			return [];
		}

		public Task<CapabilityInvocationResult> InvokeAsync(
			CapabilityInvocation invocation,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				"No variables here."));
		}
	}

	private sealed class MalformedCatalogHandler : ICapabilityHandler
	{
		public string Kind => CapabilityKinds.Variables;

		public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		{
			return [];
		}

		public Task<CapabilityInvocationResult> InvokeAsync(
			CapabilityInvocation invocation,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(invocation.Operation switch
			{
				CapabilityOperations.Variables.Describe => CapabilityInvocationResult.Ok(new VariableCatalogPayload
					{ SupportsCatalog = true, CatalogName = "Broken" }),
				CapabilityOperations.Variables.Discover => CapabilityInvocationResult.Ok(new VariableCatalogPageResult
				{
					Items = [new VariableDefinitionDto { Name = "nameless", Type = nameof(VariableType.Numeric) }]
				}),
				_ => CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported, "Not offered.")
			});
		}
	}

	private sealed class UnrelatedDescribeHandler : ICapabilityHandler
	{
		public string Kind => CapabilityKinds.Variables;

		public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		{
			return [];
		}

		public Task<CapabilityInvocationResult> InvokeAsync(
			CapabilityInvocation invocation,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(CapabilityInvocationResult.Ok("not a catalog"));
		}
	}

	private sealed class NullListsDescribeHandler : ICapabilityHandler
	{
		public string Kind => CapabilityKinds.Variables;

		public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		{
			return [];
		}

		public Task<CapabilityInvocationResult> InvokeAsync(
			CapabilityInvocation invocation,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(CapabilityInvocationResult.Ok(JsonDocument
				.Parse("""{"variables":null,"declaredVariables":null,"supportsCatalog":true}""")
				.RootElement.Clone()));
		}
	}

	private sealed class DeclaredButUnsupportedVariablesHandler : ICapabilityHandler
	{
		public string Kind => CapabilityKinds.Variables;

		public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		{
			return
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Variables,
					LocalId = "level",
					VersionRange = new CapabilityVersionRange { Minimum = 2, Maximum = 2 }
				}
			];
		}

		public Task<CapabilityInvocationResult> InvokeAsync(
			CapabilityInvocation invocation,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				"Declared, but not answered."));
		}
	}

	private sealed class RefusingWritableVariableHandler : ICapabilityHandler
	{
		public string Kind => CapabilityKinds.Variables;

		public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		{
			return [];
		}

		public Task<CapabilityInvocationResult> InvokeAsync(
			CapabilityInvocation invocation,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(invocation.Operation switch
			{
				CapabilityOperations.Variables.Describe => CapabilityInvocationResult.Ok(new VariableCatalogPayload
				{
					Variables =
					[
						new VariableDefinitionDto
						{
							Id = "level",
							Name = "level",
							Type = nameof(VariableType.Numeric),
							Write = new VariableWriteCapabilityDto()
						}
					]
				}),
				CapabilityOperations.Variables.Get => CapabilityInvocationResult.Ok(new VariableReadingDto
				{
					Value = new VariableValueDto { Kind = "number", Number = 1 }
				}),
				CapabilityOperations.Variables.Set => CapabilityInvocationResult.Ok(new VariableSetResult
				{
					Status = nameof(VariableWriteStatus.NotWritable)
				}),
				_ => CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported, "Not offered.")
			});
		}
	}
}
