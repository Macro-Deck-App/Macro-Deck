using MacroDeck.Plugin.Hosting.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class ActionsContractTests : CapabilityContractFixture
{
	private static DeclaredCapability Action(string localId)
		=> new()
		{
			Kind = CapabilityKinds.Actions, LocalId = localId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	[Test]
	public async Task Declare_registers_an_adapter_that_passes_the_capability_validator()
	{
		var integration = await ConnectAsync(
			[new ActionsCapabilityHandler([new TestIntegration(new TestAction("play"))])],
			[Action("play")],
			[CapabilityKinds.Actions]);

		Assert.Multiple(() =>
		{
			Assert.That(IntegrationCapabilityValidator.Validate(integration), Is.Empty);
			Assert.That(integration.Actions.Select(action => action.Id), Does.Contain("play"));
		});
	}

	[Test]
	public async Task Every_operation_round_trips_to_the_sdk_type_the_host_contract_requires()
	{
		var dynamic = new TestDynamicOptionsAction("pick")
		{
			ResultToReturn = new DynamicOptionsResult
			{
				Options = [new ActionParameterOption { Value = "a", Label = "A" }], AllowsCustomValue = true
			}
		};
		var plain = new TestAction("play", _ => Task.FromResult(ActionResult.Success()));

		var integration = await ConnectAsync([new ActionsCapabilityHandler([new TestIntegration(plain, dynamic)])],
			[Action("play"), Action("pick")],
			[CapabilityKinds.Actions]);

		Assert.That(integration.Actions.Single(a => a.Id == "pick"), Is.InstanceOf<IDynamicOptionsActionDefinition>());

		var executeResult = await integration.Actions.Single(a => a.Id == "play").CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = new Dictionary<string, object>() });
		Assert.That(executeResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));

		var dynamicDefinition = (IDynamicOptionsActionDefinition)integration.Actions.Single(a => a.Id == "pick");
		var options = await dynamicDefinition.GetDynamicOptionsAsync(new DynamicOptionsContext
				{ ParameterName = "value", CurrentParameters = new Dictionary<string, object?>() },
			CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(options.Options.Select(o => o.Value), Is.EqualTo(new[] { "a" }));
			Assert.That(options.AllowsCustomValue, Is.True);
		});
	}

	[Test]
	public async Task IsStateProviderAction_RoundTrips_ToTheSdkType()
	{
		var stateProvider = new TestStateProviderAction("provide-state")
		{
			SnapshotToReturn = new ActionStateSnapshot(
				[new ActionStateDefinition("off", "Off"), new ActionStateDefinition("on", "On")],
				"on")
		};

		var integration = await ConnectAsync([new ActionsCapabilityHandler([new TestIntegration(stateProvider)])],
			[Action("provide-state")],
			[CapabilityKinds.Actions]);

		var action = integration.Actions.Single(a => a.Id == "provide-state");
		Assert.That(action, Is.InstanceOf<IStateProviderActionDefinition>());

		var stateDefinition = (IStateProviderActionDefinition)action;
		var snapshot
			= await stateDefinition.GetActionStateAsync(new Dictionary<string, object?>(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(snapshot, Is.Not.Null);
			Assert.That(snapshot!.States.Select(s => (s.Id, s.Label.Literal)),
				Is.EqualTo(new[] { ("off", "Off"), ("on", "On") }));
			Assert.That(snapshot.ActiveStateId, Is.EqualTo("on"));
		});
	}

	[Test]
	public async Task An_icon_only_action_declares_icon_and_not_state()
	{
		var iconProvider = new TestIconProviderAction("provide-icon");

		var integration = await ConnectAsync([new ActionsCapabilityHandler([new TestIntegration(iconProvider)])],
			[Action("provide-icon")],
			[CapabilityKinds.Actions]);

		// Wire: catches ProvidesIcon = action is IStateProviderActionDefinition, since a copy-paste of
		// that expression would leave this true and ProvidesState below false regardless of which the
		// action actually implements.
		var remote = (RemoteActionDefinition)integration.Actions.Single(a => a.Id == "provide-icon");
		Assert.Multiple(() =>
		{
			Assert.That(remote.ProvidesIcon, Is.True);
			Assert.That(remote, Is.Not.InstanceOf<IStateProviderActionDefinition>());
		});

		// Client DTO: the same independence, one layer further out.
		var response = await new GetActionsRequestMessageHandler(IntegrationRegistry, SnapshotStore)
			.Handle(new GetActionsRequest(), CancellationToken.None);
		var actionDef = response.Actions.Single(a => a.Id == "provide-icon");

		Assert.Multiple(() =>
		{
			Assert.That(actionDef.IsIconProviderAction, Is.True);
			Assert.That(actionDef.IsStateProviderAction, Is.False);
		});
	}

	/// <summary>(Counterexample, mirror of the icon-only test above.) A state-only action - one compiled
	/// against the pre-feature SDK, like <see cref="TestStateProviderAction" /> - declares state and
	/// never icon.</summary>
	[Test]
	public async Task A_state_only_action_declares_state_and_not_icon()
	{
		var stateProvider = new TestStateProviderAction("provide-state");

		var integration = await ConnectAsync([new ActionsCapabilityHandler([new TestIntegration(stateProvider)])],
			[Action("provide-state")],
			[CapabilityKinds.Actions]);

		var remote = (RemoteActionDefinition)integration.Actions.Single(a => a.Id == "provide-state");
		Assert.Multiple(() =>
		{
			Assert.That(remote.ProvidesIcon, Is.False);
			Assert.That(remote, Is.InstanceOf<IStateProviderActionDefinition>());
		});

		var response = await new GetActionsRequestMessageHandler(IntegrationRegistry, SnapshotStore)
			.Handle(new GetActionsRequest(), CancellationToken.None);
		var actionDef = response.Actions.Single(a => a.Id == "provide-state");

		Assert.Multiple(() =>
		{
			Assert.That(actionDef.IsIconProviderAction, Is.False);
			Assert.That(actionDef.IsStateProviderAction, Is.True);
		});
	}

	/// <summary>
	/// <c>ProvidesIcon</c> reconstructs to <see cref="IIconProviderActionDefinition" /> through the
	/// standalone <see cref="RemoteIconProviderActionRegistry" /> rather than through one of
	/// <see cref="RemoteActionDefinitionFactory" />'s eight leaves - see the registry's class remarks for
	/// why. Both operations survive the wire: the polled <c>icon</c> reply and, for
	/// <c>icon.content</c>, real bytes travelling through the <c>asset.*</c> upload pipeline into this
	/// fixture's asset cache, exactly as a live host would resolve them.
	/// </summary>
	[Test]
	public async Task ProvidesIcon_RoundTrips_ToTheSdkType_ThroughTheStandaloneRegistry()
	{
		var iconProvider = new TestIconProviderAction("provide-icon")
		{
			SnapshotToReturn = new ActionIconSnapshot { Version = "etag-1" },
			ContentToReturn = new ActionIconContent([1, 2, 3], "image/png")
		};

		var integration = await ConnectAsync([new ActionsCapabilityHandler([new TestIntegration(iconProvider)])],
			[Action("provide-icon")],
			[CapabilityKinds.Actions]);

		var leaf = integration.Actions.Single(a => a.Id == "provide-icon");
		Assert.Multiple(() =>
		{
			Assert.That(((RemoteActionDefinition)leaf).ProvidesIcon, Is.True);
			Assert.That(leaf, Is.Not.InstanceOf<IIconProviderActionDefinition>());
		});

		var registry = new RemoteIconProviderActionRegistry(SnapshotStore, Invoker, AssetCache);
		var resolved = registry.Resolve(PluginId, "provide-icon");
		Assert.That(resolved, Is.Not.Null);
		Assert.That(resolved, Is.InstanceOf<IIconProviderActionDefinition>());

		var snapshot
			= await resolved!.GetActionIconAsync(new Dictionary<string, object?>(), CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(snapshot, Is.Not.Null);
			Assert.That(snapshot!.Version, Is.EqualTo("etag-1"));
			Assert.That(snapshot.Reference, Is.Null);
			Assert.That(snapshot.NoIcon, Is.False);
		});

		var content = await resolved.GetActionIconContentAsync(new Dictionary<string, object?>(),
			"etag-1",
			CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(content, Is.Not.Null);
			Assert.That(content!.Data, Is.EqualTo(new byte[] { 1, 2, 3 }));
			Assert.That(content.MediaType, Is.EqualTo("image/png"));
			Assert.That(iconProvider.ContentCallCount, Is.EqualTo(1));
		});
	}

	/// <summary>Scenario 35: a v1-only plugin (<see cref="Action" />'s declared range is always
	/// <c>{Minimum=1,Maximum=1}</c>) can be an icon provider, and the protocol major is untouched.</summary>
	[Test]
	public async Task A_v1_plugin_can_declare_the_icon_capability_without_a_protocol_major_bump()
	{
		var iconProvider = new TestIconProviderAction("provide-icon");

		var integration = await ConnectAsync([new ActionsCapabilityHandler([new TestIntegration(iconProvider)])],
			[Action("provide-icon")],
			[CapabilityKinds.Actions],
			negotiatedCapabilityVersion: 1);

		Assert.Multiple(() =>
		{
			Assert.That(IntegrationCapabilityValidator.Validate(integration), Is.Empty);
			Assert.That(((RemoteActionDefinition)integration.Actions.Single()).ProvidesIcon, Is.True);
			Assert.That(ProtocolVersions.Current, Is.EqualTo(3));
			Assert.That(ProtocolVersions.Minimum, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task ExpectedStateId_RoundTripsThroughPluginAndHostAdapters()
	{
		var action = new TestAction("toggle",
			_ => Task.FromResult(ActionResult.Accepted("Waiting for provider confirmation.", "on")));
		var integration = await ConnectAsync([new ActionsCapabilityHandler([new TestIntegration(action)])],
			[Action("toggle")],
			[CapabilityKinds.Actions]);

		var result = await integration.Actions.Single().CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = new Dictionary<string, object>() });

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Accepted));
			Assert.That(result.ExpectedStateId, Is.EqualTo("on"));
		});
	}

	[Test]
	public async Task A_timeout_degrades_to_a_failed_action_result_never_an_exception()
	{
		var integration = await ConnectAsync(
			[new ActionsCapabilityHandler([new TestIntegration(new TestAction("play", NeverReplies))])],
			[Action("play")],
			[CapabilityKinds.Actions]);

		var action = integration.Actions.Single(a => a.Id == "play");
		var executeTask = action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = new Dictionary<string, object>() });

		Time.Advance(ProtocolTimeouts.CapabilityInvoke);
		var result = await executeTask;

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ProtocolErrorCodes.Timeout));
		});
	}

	[Test]
	public async Task A_dropped_connection_degrades_to_a_failed_action_result_never_an_exception()
	{
		var integration = await ConnectAsync(
			[new ActionsCapabilityHandler([new TestIntegration(new TestAction("play"))])],
			[Action("play")],
			[CapabilityKinds.Actions]);

		Disconnect();

		var action = integration.Actions.Single(a => a.Id == "play");
		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = new Dictionary<string, object>() });

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
		});
	}

	[Test]
	public async Task Caller_cancellation_puts_capability_cancel_on_the_wire()
	{
		var stillRunning = new TaskCompletionSource<ActionResult>();
		var integration = await ConnectAsync(
			[new ActionsCapabilityHandler([new TestIntegration(new TestAction("play", _ => stillRunning.Task))])],
			[Action("play")],
			[CapabilityKinds.Actions]);

		using var cts = new CancellationTokenSource();
		var action = integration.Actions.Single(a => a.Id == "play");
		var executeTask = action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>(), CancellationToken = cts.Token
		});

		await cts.CancelAsync();

		Assert.CatchAsync<OperationCanceledException>(async () => await executeTask);
		Assert.That(Link.SentByHost.Any(envelope => envelope.Type == MessageTypes.CapabilityCancel), Is.True);

		stillRunning.TrySetResult(ActionResult.Success());
	}

	[Test]
	public async Task A_throwing_handler_yields_a_redacted_internal_error()
	{
		var integration = await ConnectAsync([
				new ActionsCapabilityHandler([
					new TestIntegration(new TestAction("play",
						_ => throw new InvalidOperationException("boom: token=abc123")))
				])
			],
			[Action("play")],
			[CapabilityKinds.Actions]);

		var action = integration.Actions.Single(a => a.Id == "play");
		var result = await action.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext { Parameters = new Dictionary<string, object>() });

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ProtocolErrorCodes.InternalError));
			Assert.That(result.ErrorMessage.Literal, Does.Not.Contain("boom"));
			Assert.That(result.ErrorMessage.Literal, Does.Not.Contain("token=abc123"));
		});
	}

	[Test]
	public async Task An_unknown_local_id_is_unavailable_and_an_unknown_operation_is_unsupported()
	{
		await ConnectAsync([new ActionsCapabilityHandler([new TestIntegration(new TestAction("play"))])],
			[Action("play")],
			[CapabilityKinds.Actions]);

		var unknownLocalId = Assert.CatchAsync<RemoteCapabilityException>(async () =>
			await InvokeRawAsync(CapabilityKinds.Actions, "nope", CapabilityOperations.Actions.Execute));
		var unknownOperation = Assert.CatchAsync<RemoteCapabilityException>(async () =>
			await InvokeRawAsync(CapabilityKinds.Actions, "play", "rewind"));

		Assert.Multiple(() =>
		{
			Assert.That(unknownLocalId!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(unknownOperation!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
		});
	}

	private static async Task<ActionResult> NeverReplies(ActionExecutionContext context)
	{
		await Task.Delay(Timeout.Infinite, context.CancellationToken);
		return ActionResult.Success();
	}
}
