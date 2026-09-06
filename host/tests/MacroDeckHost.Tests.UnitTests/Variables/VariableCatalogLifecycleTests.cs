using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;
using VariableDefinition = MacroDeck.Sdk.Variables.VariableDefinition;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
internal sealed class VariableCatalogLifecycleTests
{
	private const string IntegrationId = "com.example.smart-home";
	private const string BrightnessId = "entity/light.living_room/brightness";

	private static VariableDefinition Brightness => new()
	{
		Id = BrightnessId,
		Name = "brightness",
		Type = SdkVariableType.Numeric,
		Materialization = MacroDeck.Sdk.Variables.VariableMaterialization.OnDemand
	};

	[Test]
	public async Task A_restored_binding_regains_the_attributes_and_the_write_capability_it_was_bound_with()
	{
		// A binding persists identity only, so everything the provider declares about the variable - its
		// unit, its precision, whether it can be written at all - has to come back from the definition on
		// the next reconcile. Without that a slider bound before a restart comes back silently read-only
		// and unitless, which is a feature that works exactly until the user closes the app once.
		var declared = new VariableDefinition
		{
			Id = BrightnessId,
			Name = "brightness",
			Type = SdkVariableType.Numeric,
			Materialization = MacroDeck.Sdk.Variables.VariableMaterialization.OnDemand,
			Unit = "dB",
			SemanticKind = MacroDeck.Sdk.Variables.VariableSemanticKinds.None,
			DecimalPlaces = 1,
			Write = new MacroDeck.Sdk.Variables.VariableWriteCapability()
		};

		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(declared);
		var harness = new VariableCatalogHarness(provider);
		harness.BindingStore.Save([
			new VariableBinding
			{
				Id = Guid.CreateVersion7(),
				IntegrationId = IntegrationId,
				LocalResourceId = BrightnessId,
				Name = "living_room_brightness",
				// What the binding recorded when it was made, deliberately staler than the definition.
				Type = VariableType.Numeric,
				DecimalPlaces = 0,
				CreatedAt = DateTime.UtcNow
			}
		]);

		await harness.BindingBackgroundService.RestoreBindingsAsync(CancellationToken.None);
		await harness.Coordinator.ReconcileAsync(CancellationToken.None);

		var variable = harness.Registry.FindByName(VariableScope.Global, null, "living_room_brightness");

		Assert.Multiple(() =>
		{
			Assert.That(variable, Is.Not.Null);
			Assert.That(variable!.Name, Is.EqualTo("living_room_brightness"), "the user's name must survive");
			Assert.That(variable.Unit, Is.EqualTo("dB"));
			Assert.That(variable.DecimalPlaces, Is.EqualTo(1));
			Assert.That(variable.CanWrite, Is.True);
		});
	}

	[Test]
	public async Task A_bound_variable_is_restored_as_unavailable_when_its_provider_is_absent_at_startup()
	{
		var store = new InMemoryVariableBindingStore();
		store.Save([
			new VariableBinding
			{
				Id = Guid.CreateVersion7(),
				IntegrationId = IntegrationId,
				LocalResourceId = BrightnessId,
				Name = "living_room_brightness",
				Type = VariableType.Numeric,
				CreatedAt = DateTime.UtcNow
			}
		]);

		// No provider registered at all - the integration is not present.
		var harness = new VariableCatalogHarness();
		harness.BindingStore.Save(store.Load());

		await harness.BindingBackgroundService.RestoreBindingsAsync(CancellationToken.None);

		var variable = harness.Registry.FindByName(VariableScope.Global, null, "living_room_brightness");

		Assert.Multiple(() =>
		{
			Assert.That(variable, Is.Not.Null);
			Assert.That(variable!.DefinitionId, Is.EqualTo(BrightnessId));
			Assert.That(harness.Registry.IsAvailable(variable.Id), Is.False);

			var renderer = new VariableTemplateRenderer(harness.Registry);
			var rendered = renderer.Render("{{ vars.living_room_brightness }}",
				renderer.CreateContextAsync(VariableScope.Global, null).Result);
			Assert.That(rendered, Is.EqualTo(VariableTemplateRenderer.UnavailablePlaceholder));

			Assert.That(harness.BindingStore.Load(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_bound_variable_resumes_automatically_when_its_resource_reappears()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId, IsInitialized = false };
		provider.AddDefinition(Brightness);
		var harness = new VariableCatalogHarness(provider);

		harness.BindingStore.Save([
			new VariableBinding
			{
				Id = Guid.CreateVersion7(),
				IntegrationId = IntegrationId,
				LocalResourceId = BrightnessId,
				Name = "living_room_brightness",
				Type = VariableType.Numeric,
				CreatedAt = DateTime.UtcNow
			}
		]);

		await harness.BindingBackgroundService.RestoreBindingsAsync(CancellationToken.None);
		var variable = harness.Registry.FindByName(VariableScope.Global, null, "living_room_brightness")!;
		Assert.That(harness.Registry.IsAvailable(variable.Id), Is.False);

		// The provider registers and becomes usable.
		provider.IsInitialized = true;
		provider.SetValue(BrightnessId, 77m);
		await harness.Coordinator.ReconcileAsync(CancellationToken.None);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);

		var afterReconnect = harness.Registry.FindByName(VariableScope.Global, null, "living_room_brightness");

		Assert.Multiple(() =>
		{
			Assert.That(afterReconnect, Is.Not.Null);
			Assert.That(afterReconnect!.Id, Is.EqualTo(variable.Id));
			Assert.That(afterReconnect.DefinitionId, Is.EqualTo(BrightnessId));
			Assert.That(harness.Registry.IsAvailable(afterReconnect.Id), Is.True);
			Assert.That(afterReconnect.Value, Is.EqualTo("77"));
		});
	}

	[Test]
	public async Task A_disappearing_resource_marks_the_variable_unavailable_without_removing_configuration()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(Brightness);
		provider.SetValue(BrightnessId, 5m);
		var harness = new VariableCatalogHarness(provider);
		var mediator = harness.Mediator;

		var bound = await harness.BindingService.BindAsync(IntegrationId, BrightnessId, null, null);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);
		mediator.Published.Clear();

		harness.Channel.Write(IntegrationId, BrightnessId, null);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);

		var variable = harness.Registry.GetById(bound.Data!.Id);

		Assert.Multiple(() =>
		{
			Assert.That(variable, Is.Not.Null, "the variable must still exist");
			Assert.That(harness.Registry.IsAvailable(bound.Data!.Id), Is.False);
			Assert.That(harness.BindingStore.Load(), Has.Count.EqualTo(1), "the binding must be untouched");
			Assert.That(mediator.Published.OfType<VariableDeletedNotification>(), Is.Empty);
		});
	}

	[Test]
	public async Task A_returning_resource_republishes_even_when_its_value_did_not_change()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(Brightness);
		provider.SetValue(BrightnessId, 5m);
		var harness = new VariableCatalogHarness(provider);
		var mediator = harness.Mediator;

		var bound = await harness.BindingService.BindAsync(IntegrationId, BrightnessId, null, null);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);

		harness.Channel.Write(IntegrationId, BrightnessId, null);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);
		mediator.Published.Clear();

		harness.Channel.Write(IntegrationId, BrightnessId, 5m);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Registry.IsAvailable(bound.Data!.Id), Is.True);
			Assert.That(mediator.Published.OfType<VariableUpdatedNotification>(), Is.Not.Empty);
			Assert.That(mediator.Published.OfType<VariableValueChangedNotification>(), Is.Not.Empty);
		});
	}

	[Test]
	public async Task A_pushed_variable_stays_available_past_the_freshness_window()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId, SupportsPush = true };
		provider.AddDefinition(Brightness);
		provider.SetValue(BrightnessId, 5m);
		var harness = new VariableCatalogHarness(provider);

		var bound = await harness.BindingService.BindAsync(IntegrationId, BrightnessId, null, null);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);

		var variable = harness.Registry.GetById(bound.Data!.Id)!;
		variable.UpdatedAt = DateTime.UtcNow - VariableRegistry.IntegrationFreshness - TimeSpan.FromMinutes(5);
		harness.Registry.Upsert(variable);

		Assert.That(harness.Registry.IsAvailable(bound.Data!.Id), Is.True);
	}

	[Test]
	public async Task Disabling_an_integration_unsubscribes_and_re_enabling_resubscribes_without_user_action()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(Brightness);
		provider.SetValue(BrightnessId, 5m);
		var harness = new VariableCatalogHarness(provider);

		var bound = await harness.BindingService.BindAsync(IntegrationId, BrightnessId, null, null);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);
		Assert.That(harness.Registry.IsAvailable(bound.Data!.Id), Is.True);

		var handler = CreateStateChangedHandler(harness);

		harness.Integrations.SetEnabled(IntegrationId, false);
		await handler.Handle(new IntegrationStateChangedNotification(IntegrationId), CancellationToken.None);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);

		Assert.That(harness.Registry.IsAvailable(bound.Data!.Id), Is.False, "disabling must unsubscribe");

		harness.Integrations.SetEnabled(IntegrationId, true);
		provider.SetValue(BrightnessId, 9m);
		await handler.Handle(new IntegrationStateChangedNotification(IntegrationId), CancellationToken.None);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);

		var variable = harness.Registry.GetById(bound.Data!.Id);
		Assert.Multiple(() =>
		{
			Assert.That(harness.Registry.IsAvailable(bound.Data!.Id),
				Is.True,
				"re-enabling must resubscribe without user action");
			Assert.That(variable!.Value, Is.EqualTo("9"));
		});
	}

	[Test]
	public async Task A_replaced_provider_instance_is_reattached_and_its_pushes_reach_the_registry()
	{
		// RemotePluginIntegration replaces its adapter instance on every snapshot refresh; the coordinator
		// must attach the new instance rather than assuming "this integration id was attached before" is
		// enough. Unregister/register (rather than mutating the original provider) is what actually
		// swaps the instance the way a snapshot refresh does.
		var first = new FakeVariableProviderIntegration { Id = IntegrationId };
		first.AddDefinition(Brightness);
		first.SetValue(BrightnessId, 5m);
		var harness = new VariableCatalogHarness(first);

		var bound = await harness.BindingService.BindAsync(IntegrationId, BrightnessId, null, null);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);
		Assert.That(first.AttachCount, Is.EqualTo(1));
		Assert.That(harness.Registry.IsAvailable(bound.Data!.Id), Is.True);

		var second = new FakeVariableProviderIntegration { Id = IntegrationId };
		second.AddDefinition(Brightness);
		second.SetValue(BrightnessId, 42m);

		Assert.That(await harness.Integrations.UnregisterAsync(IntegrationId), Is.True);
		await harness.Integrations.RegisterAsync(second);

		await harness.Coordinator.ReconcileAsync(CancellationToken.None);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(second.AttachCount, Is.EqualTo(1), "the new instance must be attached exactly once");
			Assert.That(second.AttachedSink, Is.Not.Null);
		});

		// A push through the *new* instance's sink must actually reach the registry - if the coordinator
		// had kept treating the integration id as already-attached, second.AttachedSink would be null (or
		// unused) and this push would go nowhere.
		await second.AttachedSink!.PublishAsync(MacroDeck.Sdk.Variables.VariableValue.Of(BrightnessId, 99m),
			CancellationToken.None);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);

		var variable = harness.Registry.GetById(bound.Data!.Id);
		Assert.That(variable!.Value, Is.EqualTo("99"));
	}

	private static IntegrationStateChangedNotificationHandler CreateStateChangedHandler(VariableCatalogHarness harness)
		=> new(new NoOpWeatherBroadcastTrigger(),
			new NoOpIssueBroadcastTrigger(),
			new VariablePollingInvalidationSignal(),
			harness.Coordinator,
			new NoOpWidgetVariableIndex(),
			new WidgetStateEvalChannel());

	private sealed class NoOpWeatherBroadcastTrigger : IWeatherBroadcastTrigger
	{
		public void RequestRefresh()
		{
		}

		public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(false);
	}

	private sealed class
		NoOpIssueBroadcastTrigger : MacroDeckHost.Application.Integrations.IIntegrationIssueBroadcastTrigger
	{
		public void RequestRefresh()
		{
		}

		public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) => Task.FromResult(false);
	}

	private sealed class NoOpWidgetVariableIndex : IWidgetVariableIndex
	{
		public IReadOnlyList<Guid> FindLabelReferences(string variableName) => [];

		public IReadOnlyList<Guid> FindStateMappingReferences(string variableName) => [];

		public IReadOnlyList<Guid> FindProviderReferences(string integrationId, string? actionId = null) => [];

		public IReadOnlyList<Guid> FindIconProviderReferences(string integrationId, string? actionId = null) => [];

		public bool LabelReferences(Guid widgetId, string variableName) => false;

		public bool StateMappingReferences(Guid widgetId, string variableName) => false;

		public void Rebuild()
		{
		}

		public void ReindexWidget(Guid widgetId, string type, string? data)
		{
		}

		public void Remove(Guid widgetId)
		{
		}
	}
}
