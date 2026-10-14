using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class LateVariablesContractTests : CapabilityContractFixture
{
	private const string LevelId = "late-level";

	private static readonly VariableDefinition _level = VariableDefinition.Eager("late_level", VariableType.Numeric)
		with
		{
			Id = LevelId,
			Write = new VariableWriteCapability()
		};

	[Test]
	public async Task A_writable_variable_announced_after_registration_accepts_writes()
	{
		var provider = new LateVariableIntegration();
		await ConnectAsTheSdkDeclaresAsync([Handler(provider)]);

		provider.Add(_level);
		await AnnounceAsync(CapabilityKinds.Variables);

		var owner = new VariableCatalogProviders(IntegrationRegistry).ResolveOwner(PluginId);
		Assert.That(owner, Is.Not.Null, "the plugin must own its announced variables");

		var result = await owner!.SetValueAsync(LevelId, 42.0, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
			Assert.That(provider.Writes, Is.EqualTo(new[] { $"{LevelId}=42" }));
		});
	}

	[Test]
	public async Task A_variable_announced_while_the_plugin_is_still_registering_accepts_writes()
	{
		var provider = new LateVariableIntegration();
		var actions = new BlockingActionsHandler();

		await ConnectAsTheSdkDeclaresAsync([Handler(provider), actions],
			async () =>
			{
				await actions.Describing.Task.WaitAsync(TimeSpan.FromSeconds(10));
				provider.Add(_level);
				await AnnounceAsync(CapabilityKinds.Variables);
				actions.Release.SetResult();
			});

		var owner = new VariableCatalogProviders(IntegrationRegistry).ResolveOwner(PluginId);
		Assert.That(owner, Is.Not.Null, "an announcement during registration must not be lost");

		var result = await owner!.SetValueAsync(LevelId, 7.0, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
			Assert.That(((IVariableProvider)owner).Variables.Select(v => v.Name), Does.Contain("late_level"));
			Assert.That(SnapshotStore.GetSnapshot(PluginId).AcceptedKinds, Does.Contain(CapabilityKinds.Variables));
		});
	}

	[Test]
	public async Task Announced_variables_stay_writable_when_the_same_session_registers_again()
	{
		var provider = new LateVariableIntegration();
		await ConnectAsTheSdkDeclaresAsync([Handler(provider)]);
		provider.Add(_level);
		await AnnounceAsync(CapabilityKinds.Variables);

		Assert.That(await Registrar.RegisterAsync(PluginId, CancellationToken.None), Is.True);

		var owner = new VariableCatalogProviders(IntegrationRegistry).ResolveOwner(PluginId);
		Assert.That(owner, Is.Not.Null, "a resumed session sends no new announcement");

		var result = await owner!.SetValueAsync(LevelId, 3.0, CancellationToken.None);

		Assert.That(result.Status, Is.EqualTo(VariableWriteStatus.Applied));
	}

	[Test]
	public async Task A_provider_that_is_still_empty_after_announcing_does_not_offer_variables()
	{
		var provider = new LateVariableIntegration();
		await ConnectAsTheSdkDeclaresAsync([Handler(provider)]);

		await AnnounceAsync(CapabilityKinds.Variables);

		Assert.Multiple(() =>
		{
			Assert.That(new VariableCatalogProviders(IntegrationRegistry).ResolveOwner(PluginId), Is.Null);
			Assert.That(SnapshotStore.GetSnapshot(PluginId).AcceptedKinds,
				Does.Not.Contain(CapabilityKinds.Variables));
		});
	}

	[Test]
	public async Task Variables_rejected_at_negotiation_stay_unavailable_after_an_announcement()
	{
		var provider = new LateVariableIntegration();
		DeclaredCapability unsupported = new()
		{
			Kind = CapabilityKinds.Variables,
			LocalId = "future-variable",
			VersionRange = new CapabilityVersionRange { Minimum = 99, Maximum = 99 }
		};
		Assert.That(CapabilityVersionNegotiator.Negotiate(unsupported.Kind, unsupported.VersionRange).Accepted,
			Is.False);

		await ConnectAsync([Handler(provider)], [unsupported], []);

		provider.Add(_level);
		await AnnounceAsync(CapabilityKinds.Variables);

		Assert.That(new VariableCatalogProviders(IntegrationRegistry).ResolveOwner(PluginId), Is.Null);
	}

	private async Task AnnounceAsync(string kind)
	{
		var handled = Link.StateUpdatesHandled;
		await SendStateUpdateFromPluginAsync(kind);
		await WaitForAsync(() => Link.StateUpdatesHandled > handled,
			"the host never received the state.update");
		await Link.LastStateUpdate;
	}

	private static VariablesCapabilityHandler Handler(IPluginIntegration provider)
	{
		return new VariablesCapabilityHandler([provider],
			TestMetadata.Default,
			new VariableSubscriptions(Serilog.Log.Logger),
			Serilog.Core.Logger.None);
	}

	private sealed class LateVariableIntegration : IPluginIntegration, IVariableProvider
	{
		private readonly List<VariableDefinition> _variables = [];

		public List<string> Writes { get; } = [];

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public IReadOnlyList<VariableDefinition> Variables => [.. _variables];

		public void Add(VariableDefinition variable)
		{
			_variables.Add(variable);
		}

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

		public ValueTask<VariableWriteResult> SetValueAsync(
			string localId,
			object? value,
			CancellationToken cancellationToken = default)
		{
			Writes.Add($"{localId}={value}");
			return ValueTask.FromResult(VariableWriteResult.Applied());
		}
	}

	private sealed class BlockingActionsHandler : ICapabilityHandler
	{
		private const string DescribeJson =
			"""{"actions":[{"localId":"noop","name":"Noop","description":"Does nothing","parameters":[],"supportsDynamicOptions":false,"providesState":false}]}""";

		public TaskCompletionSource Describing { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public string Kind => CapabilityKinds.Actions;

		public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		{
			return
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Actions,
					LocalId = "noop",
					VersionRange = new CapabilityVersionRange { Minimum = 3, Maximum = 3 }
				}
			];
		}

		public async Task<CapabilityInvocationResult> InvokeAsync(
			CapabilityInvocation invocation,
			CancellationToken cancellationToken)
		{
			Describing.TrySetResult();
			await Release.Task.WaitAsync(cancellationToken);
			return CapabilityInvocationResult.Ok(JsonDocument.Parse(DescribeJson).RootElement.Clone());
		}
	}
}
