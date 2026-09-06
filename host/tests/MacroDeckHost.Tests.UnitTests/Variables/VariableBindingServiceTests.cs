using MacroDeckHost.Domain.Enums;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;
using VariableDefinition = MacroDeck.Sdk.Variables.VariableDefinition;
using VariableValue = MacroDeck.Sdk.Variables.VariableValue;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
internal sealed class VariableBindingServiceTests
{
	private const string IntegrationId = "com.example.smart-home";
	private const string BrightnessId = "entity/light.living_room/brightness";
	private const string StateId = "entity/light.living_room/state";

	private static VariableDefinition Brightness => new()
	{
		Id = BrightnessId,
		Name = "brightness",
		Type = SdkVariableType.Numeric,
		Materialization = MacroDeck.Sdk.Variables.VariableMaterialization.OnDemand
	};

	private static VariableDefinition LightState => new()
	{
		Id = StateId,
		Name = "state",
		Type = SdkVariableType.Text,
		Materialization = MacroDeck.Sdk.Variables.VariableMaterialization.OnDemand
	};

	[Test]
	public async Task Binding_an_unresolvable_id_creates_nothing()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		var harness = new VariableCatalogHarness(provider);

		var result = await harness.BindingService.BindAsync(IntegrationId, "no/such/resource", null, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(harness.BindingStore.Load(), Is.Empty);
			Assert.That(harness.Registry.GetAll(), Is.Empty);
		});
	}

	[Test]
	public async Task Binding_a_resolvable_but_currently_unreadable_id_succeeds()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(Brightness);
		// No value configured: GetValueAsync returns null, exercising the "resolvable but unreadable" path.
		var harness = new VariableCatalogHarness(provider);

		var result = await harness.BindingService.BindAsync(IntegrationId, BrightnessId, null, null);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var variable = harness.Registry.GetById(result.Data!.Id);
		Assert.That(variable, Is.Not.Null);
		Assert.That(harness.Registry.IsAvailable(variable!.Id), Is.False);
	}

	[Test]
	public async Task Two_resources_whose_derived_names_collide_get_distinct_names()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(new VariableDefinition
		{
			Id = "a/x",
			Name = "light",
			Type = SdkVariableType.Text,
			Materialization = MacroDeck.Sdk.Variables.VariableMaterialization.OnDemand
		});
		provider.AddDefinition(new VariableDefinition
		{
			Id = "a/y",
			Name = "light",
			Type = SdkVariableType.Text,
			Materialization = MacroDeck.Sdk.Variables.VariableMaterialization.OnDemand
		});
		var harness = new VariableCatalogHarness(provider);
		var service = harness.BindingService;

		var first = await service.BindAsync(IntegrationId, "a/x", null, null);
		var firstNameAfterFirstBind = first.Data!.Name;

		var second = await service.BindAsync(IntegrationId, "a/y", null, null);

		Assert.Multiple(() =>
		{
			Assert.That(first.Success, Is.True);
			Assert.That(second.Success, Is.True);
			Assert.That(first.Data!.Name, Does.Match("^[a-z][a-z0-9_]*$"));
			Assert.That(second.Data!.Name, Does.Match("^[a-z][a-z0-9_]*$"));
			Assert.That(first.Data!.Name, Is.Not.EqualTo(second.Data!.Name));
			Assert.That(first.Data!.Name,
				Is.EqualTo(firstNameAfterFirstBind),
				"binding the second resource must not rename the first");
		});
	}

	[Test]
	public async Task Derived_names_are_stable_across_a_restart_regardless_of_load_order()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(new VariableDefinition
		{
			Id = "a/x",
			Name = "light",
			Type = SdkVariableType.Text,
			Materialization = MacroDeck.Sdk.Variables.VariableMaterialization.OnDemand
		});
		provider.AddDefinition(new VariableDefinition
		{
			Id = "a/y",
			Name = "light",
			Type = SdkVariableType.Text,
			Materialization = MacroDeck.Sdk.Variables.VariableMaterialization.OnDemand
		});
		var harness = new VariableCatalogHarness(provider);
		var service = harness.BindingService;

		var first = await service.BindAsync(IntegrationId, "a/x", null, null);
		var second = await service.BindAsync(IntegrationId, "a/y", null, null);
		var savedBindings = harness.BindingStore.Load();

		// Reload the same two bindings in the opposite order into a fresh registry.
		var reloadedProvider = new FakeVariableProviderIntegration { Id = IntegrationId };
		var reloadedHarness = new VariableCatalogHarness(reloadedProvider);
		reloadedHarness.BindingStore.Save(savedBindings.Reverse());

		await reloadedHarness.BindingBackgroundService.RestoreBindingsAsync(CancellationToken.None);

		var reloadedX = reloadedHarness.Registry.FindByName(VariableScope.Global, null, first.Data!.Name);
		var reloadedY = reloadedHarness.Registry.FindByName(VariableScope.Global, null, second.Data!.Name);

		Assert.Multiple(() =>
		{
			Assert.That(reloadedX, Is.Not.Null);
			Assert.That(reloadedY, Is.Not.Null);
			Assert.That(reloadedX!.DefinitionId, Is.EqualTo("a/x"));
			Assert.That(reloadedY!.DefinitionId, Is.EqualTo("a/y"));
		});
	}

	[Test]
	public async Task Unbinding_removes_both_the_variable_and_the_binding_and_it_stays_gone_across_a_restart()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(Brightness);
		var harness = new VariableCatalogHarness(provider);
		var service = harness.BindingService;

		var bound = await service.BindAsync(IntegrationId, BrightnessId, null, null);
		Assert.That(bound.Success, Is.True);

		var unbind = await service.UnbindAsync(bound.Data!.Id);

		Assert.Multiple(() =>
		{
			Assert.That(unbind.Success, Is.True);
			Assert.That(harness.Registry.GetById(bound.Data!.Id), Is.Null);
			Assert.That(harness.BindingStore.Load(), Is.Empty);
		});

		await harness.BindingBackgroundService.RestoreBindingsAsync(CancellationToken.None);
		Assert.That(harness.Registry.GetAll(), Is.Empty, "an unbound resource must not come back on restart");
	}

	[Test]
	public async Task Only_bound_resources_are_subscribed()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		for (var i = 0; i < 1000; i++)
		{
			provider.AddDefinition(new VariableDefinition
			{
				Id = $"entity/sensor.{i}",
				Name = $"sensor_{i}",
				Type = SdkVariableType.Text,
				Materialization = MacroDeck.Sdk.Variables.VariableMaterialization.OnDemand
			});
		}

		provider.AddDefinition(Brightness);
		provider.AddDefinition(LightState);

		var harness = new VariableCatalogHarness(provider);
		var service = harness.BindingService;

		await service.BindAsync(IntegrationId, BrightnessId, null, null);
		await service.BindAsync(IntegrationId, StateId, null, null);

		var lastSubscribeCall = provider.SubscribeCalls.Last();

		Assert.Multiple(() =>
		{
			Assert.That(lastSubscribeCall, Is.EquivalentTo(new[] { BrightnessId, StateId }));
			Assert.That(provider.GetValueCalls, Does.Not.Contain("entity/sensor.0"));
			Assert.That(provider.GetValueCalls, Does.Not.Contain("entity/sensor.999"));
		});
	}

	[Test]
	public async Task Unbinding_the_last_resource_tells_the_provider_to_watch_nothing()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(Brightness);
		var harness = new VariableCatalogHarness(provider);
		var service = harness.BindingService;

		var bound = await service.BindAsync(IntegrationId, BrightnessId, null, null);
		await service.UnbindAsync(bound.Data!.Id);

		var lastSubscribeCall = provider.SubscribeCalls.Last();
		Assert.That(lastSubscribeCall, Is.Empty);
	}

	[Test]
	public async Task A_subscribe_result_seeds_the_value_without_a_second_read()
	{
		var provider = new FakeVariableProviderIntegration { Id = IntegrationId };
		provider.AddDefinition(Brightness);
		provider.SubscribeResponder = ids => ids
			.Select(id => VariableValue.Of(id, 42m))
			.ToList();

		var harness = new VariableCatalogHarness(provider);
		var result = await harness.BindingService.BindAsync(IntegrationId, BrightnessId, null, null);
		await harness.UpdateService.ApplyPendingAsync(CancellationToken.None);

		var variable = harness.Registry.GetById(result.Data!.Id);

		Assert.Multiple(() =>
		{
			Assert.That(variable, Is.Not.Null);
			Assert.That(harness.Registry.IsAvailable(variable!.Id), Is.True);
			Assert.That(variable.Value, Is.EqualTo("42"));
			Assert.That(provider.GetValueCalls, Does.Not.Contain(BrightnessId));
		});
	}
}
