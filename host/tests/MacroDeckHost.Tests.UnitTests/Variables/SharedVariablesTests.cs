using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Ui;
using VariableDefinition = MacroDeck.Sdk.Variables.VariableDefinition;
using VariableWriteCapability = MacroDeck.Sdk.Variables.VariableWriteCapability;
using VariableWriteResult = MacroDeck.Sdk.Variables.VariableWriteResult;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
internal sealed class SharedVariablesTests
{
	private const string ObsIntegration = "app.macro-deck.obs";

	private VariableRegistry _registry = null!;
	private RecordingMediator _mediator = null!;
	private VariableService _service = null!;
	private InMemorySharedVariableStore _store = null!;
	private SharedVariables _shared = null!;
	private ConfigurableIntegrationRegistry _integrations = null!;

	[SetUp]
	public void SetUp()
	{
		_registry = new VariableRegistry();
		_mediator = new RecordingMediator();
		_integrations = new ConfigurableIntegrationRegistry([]);
		_service = TestVariableServices.Create(_registry, new NullUserVariableStore(), _mediator, _integrations);
		_store = new InMemorySharedVariableStore();
		_shared = InMemorySharedVariableStore.For(_registry, _store);
	}

	[Test]
	public async Task A_shared_user_variable_is_listed_with_its_value_and_reported_as_shared()
	{
		var deaths = await CreateUser("deaths", VariableType.Numeric, 3m);

		var toggle = await SetShared(deaths, true);
		var listed = await ListAsync();
		var all = await GetAllAsync();

		Assert.Multiple(() =>
		{
			Assert.That(toggle.Success, Is.True);
			Assert.That(toggle.Variable!.Shared, Is.True);
			Assert.That(listed.Select(v => (v.Name, v.Type, v.Value, v.Present)),
				Is.EqualTo(new[] { ("deaths", "numeric", "3", true) }));
			Assert.That(all.Single(v => v.Name == "deaths").Shared, Is.True);
			Assert.That(_mediator.Published.OfType<VariableUpdatedNotification>().Select(n => n.Variable.Id),
				Does.Contain(deaths.Id),
				"clients must learn about the new flag");
		});
	}

	[Test]
	public async Task Unsharing_removes_the_variable_from_the_list()
	{
		var deaths = await CreateUser("deaths", VariableType.Numeric, 3m);
		await SetShared(deaths, true);

		await SetShared(deaths, false);

		Assert.That(await ListAsync(), Is.Empty);
	}

	[Test]
	public async Task A_renamed_user_variable_stays_shared_under_its_new_name()
	{
		var deaths = await CreateUser("deaths", VariableType.Numeric, 3m);
		await SetShared(deaths, true);

		await _service.UpdateUserVariable(deaths.Id, "boss_deaths", null);

		Assert.That((await ListAsync()).Select(v => v.Name), Is.EqualTo(new[] { "boss_deaths" }));
	}

	[Test]
	public async Task A_deleted_user_variable_is_not_shared_again_by_a_new_one_with_the_same_name()
	{
		var deaths = await CreateUser("deaths", VariableType.Numeric, 3m);
		await SetShared(deaths, true);

		await _service.DeleteUserVariable(deaths.Id);
		foreach (var deleted in _mediator.Published.OfType<VariableDeletedNotification>().ToList())
		{
			await new SharedVariableDeletedHandler(_shared).Handle(deleted, CancellationToken.None);
		}

		var recreated = await CreateUser("deaths", VariableType.Numeric, 0m);

		Assert.Multiple(() =>
		{
			Assert.That(_shared.IsShared(recreated), Is.False);
			Assert.That(_store.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task A_shared_integration_variable_stays_listed_while_it_is_gone_and_is_shared_again_when_it_returns()
	{
		var streaming = await CreateIntegration("obs_streaming", "obs-streaming");
		await SetShared(streaming, true);

		await _service.DeleteIntegrationVariable(ObsIntegration, streaming.Id);
		var whileGone = await ListAsync();

		var returned = await CreateIntegration("obs_streaming", "obs-streaming");
		var afterReturn = await ListAsync();

		Assert.Multiple(() =>
		{
			Assert.That(whileGone.Select(v => (v.Name, v.Type, v.Present, v.Available)),
				Is.EqualTo(new[] { ("obs_streaming", "boolean", false, false) }));
			Assert.That(_shared.IsShared(returned), Is.True);
			Assert.That(afterReturn.Single().Present, Is.True);
		});
	}

	[Test]
	public async Task A_widget_variable_cannot_be_shared()
	{
		var result = await _service.CreateUserVariable("counter",
			VariableScope.Widget,
			"widget-1",
			VariableType.Numeric,
			0m,
			null);

		var toggle = await SetShared(result.Data!, true);

		Assert.Multiple(() =>
		{
			Assert.That(toggle.Success, Is.False);
			Assert.That(_store.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task A_variable_its_provider_declared_non_shareable_cannot_be_shared()
	{
		var mirrored = await CreateDeclared("mirrored", "mirrored", false);

		var toggle = await SetShared(mirrored, true);

		Assert.Multiple(() =>
		{
			Assert.That(toggle.Success, Is.False);
			Assert.That(toggle.Error!.Code, Is.EqualTo("NotEditable"));
			Assert.That(TestLocalization.Resolve(toggle.Error.Message), Is.EqualTo("This variable cannot be shared."));
			Assert.That(_store.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task A_variable_shared_before_its_provider_declared_it_non_shareable_is_never_served()
	{
		var scene = await CreateDeclared("obs_scene", "obs-scene", true);
		await SetShared(scene, true);

		var redeclared = await CreateDeclared("obs_scene", "obs-scene", false);
		var listed = await ListAsync();
		var write = await SetValue("obs_scene", "Main");

		Assert.Multiple(() =>
		{
			Assert.That(redeclared.Id, Is.EqualTo(scene.Id));
			Assert.That(listed, Is.Empty);
			Assert.That(_shared.IsShared(redeclared), Is.False);
			Assert.That(write.Success, Is.False);
		});
	}

	[Test]
	public async Task A_share_stored_before_its_provider_declared_it_non_shareable_can_still_be_removed()
	{
		var scene = await CreateDeclared("obs_scene", "obs-scene", true);
		await SetShared(scene, true);
		await CreateDeclared("obs_scene", "obs-scene", false);

		var toggle = await SetShared(scene, false);

		Assert.Multiple(() =>
		{
			Assert.That(toggle.Success, Is.True);
			Assert.That(_store.Entries, Is.Empty);
		});
	}

	[Test]
	public async Task A_shared_variable_can_be_written_by_name()
	{
		var deaths = await CreateUser("deaths", VariableType.Numeric, 3m);
		await SetShared(deaths, true);

		var response = await SetValue("deaths", "4.5");

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(_registry.GetById(deaths.Id)!.Value, Is.EqualTo("4.5"));
		});
	}

	[Test]
	public async Task A_variable_that_is_not_shared_cannot_be_written_by_name()
	{
		var deaths = await CreateUser("deaths", VariableType.Numeric, 3m);

		var response = await SetValue("deaths", "4");

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(_registry.GetById(deaths.Id)!.Value, Is.EqualTo("3"));
		});
	}

	[Test]
	public async Task A_value_that_does_not_fit_the_type_is_refused_instead_of_becoming_zero()
	{
		var deaths = await CreateUser("deaths", VariableType.Numeric, 3m);
		await SetShared(deaths, true);

		var response = await SetValue("deaths", "many");

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo("InvalidValue"));
			Assert.That(_registry.GetById(deaths.Id)!.Value, Is.EqualTo("3"));
		});
	}

	[Test]
	public async Task A_locked_host_refuses_a_write_by_name()
	{
		var deaths = await CreateUser("deaths", VariableType.Numeric, 3m);
		await SetShared(deaths, true);

		var response = await new SetSharedVariableValueRequestMessageHandler(_shared,
				_service,
				new FakeHostLockState { IsLocked = true },
				OwnerNames())
			.Handle(new SetSharedVariableValueRequest { Name = "deaths", Value = "4" }, CancellationToken.None);

		Assert.That(response.Error!.Code, Is.EqualTo("HOST_LOCKED"));
	}

	[Test]
	public async Task A_value_broadcast_keeps_the_shared_flag()
	{
		var deaths = await CreateUser("deaths", VariableType.Numeric, 3m);
		await SetShared(deaths, true);

		var snapshot = new VariableBroadcaster(_registry,
				new VariableInterestTracker(),
				null!,
				new VariableBindingLookup(_registry, new InMemoryVariableBindingStore()),
				Serilog.Core.Logger.None,
				_shared)
			.Snapshot(["deaths"]);

		Assert.That(snapshot.Upserted.Single().Shared, Is.True);
	}

	[Test]
	public async Task A_write_by_name_to_an_unavailable_owner_names_the_owner_by_its_display_name()
	{
		var provider = new FakeWritableVariableProviderIntegration
		{
			Id = "app.example.mixer",
			Name = "Volume Mixer",
			IsInitialized = true,
			Answer = VariableWriteResult.Unavailable("offline"),
			Variables =
			[
				VariableDefinition.Eager("volume", MacroDeck.Sdk.Variables.VariableType.Numeric) with
				{
					Id = "volume", Write = new VariableWriteCapability()
				}
			]
		};
		await _integrations.RegisterAsync(provider);
		var volume = (await _service.CreateIntegrationVariable(provider.Id,
			"mixer_volume",
			VariableScope.Global,
			null,
			VariableType.Numeric,
			10m,
			0,
			"volume",
			new VariableDeclaration { Write = new VariableWriteCapability() })).Data!;
		await SetShared(volume, true);

		var response = await SetValue("mixer_volume", "42");

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(VariableError.OwnerUnavailable)));
			Assert.That(response.Error.Message.Localized?.Key,
				Is.EqualTo(AppStrings.Errors.Variables.OwnerUnavailable("Volume Mixer").Key));
			Assert.That(TestLocalization.Resolve(response.Error.Message),
				Is.EqualTo("Volume Mixer is not available right now"));
		});
	}

	[Test]
	public async Task The_flags_are_read_back_from_the_store_on_a_new_start()
	{
		var deaths = await CreateUser("deaths", VariableType.Numeric, 3m);
		await SetShared(deaths, true);

		var restarted = InMemorySharedVariableStore.For(_registry, _store);

		Assert.That(restarted.IsShared(deaths), Is.True);
	}

	private VariableOwnerNames OwnerNames()
	{
		return new VariableOwnerNames(_integrations, TestLocalization.ScopeFactory);
	}

	private async Task<VariableEntity> CreateUser(string name, VariableType type, object value)
	{
		return (await _service.CreateUserVariable(name, VariableScope.Global, null, type, value, null)).Data!;
	}

	private async Task<VariableEntity> CreateIntegration(string name, string definitionId)
	{
		return (await _service.CreateIntegrationVariable(ObsIntegration,
			name,
			VariableScope.Global,
			null,
			VariableType.Boolean,
			true,
			null,
			definitionId)).Data!;
	}

	private async Task<VariableEntity> CreateDeclared(string name, string definitionId, bool shareable)
	{
		var definition = VariableDefinition.Eager(name, MacroDeck.Sdk.Variables.VariableType.Text) with
		{
			Id = definitionId, IsShareable = shareable
		};
		return (await _service.CreateIntegrationVariable(ObsIntegration,
			name,
			VariableScope.Global,
			null,
			VariableType.Text,
			"Intro",
			null,
			definitionId,
			VariableDeclarationFactory.From(definition))).Data!;
	}

	private async Task<SetVariableSharedResponse> SetShared(VariableEntity variable, bool shared)
	{
		return await new SetVariableSharedRequestMessageHandler(_shared, _registry, _mediator)
			.Handle(new SetVariableSharedRequest { Id = variable.Id.ToString(), Shared = shared },
				CancellationToken.None);
	}

	private async Task<List<SharedVariableDto>> ListAsync()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return (await new GetSharedVariablesRequestMessageHandler(_shared, _registry, readiness)
			.Handle(new GetSharedVariablesRequest(), CancellationToken.None)).Variables;
	}

	private async Task<List<Variable>> GetAllAsync()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return (await new GetVariablesRequestMessageHandler(_service,
				_registry,
				new FakeVariableBindingService(),
				readiness,
				_shared)
			.Handle(new GetVariablesRequest(), CancellationToken.None)).Variables;
	}

	private async Task<SetSharedVariableValueResponse> SetValue(string name, string value)
	{
		return await new SetSharedVariableValueRequestMessageHandler(_shared,
				_service,
				new FakeHostLockState { IsLocked = false },
				OwnerNames())
			.Handle(new SetSharedVariableValueRequest { Name = name, Value = value }, CancellationToken.None);
	}
}
