using MacroDeckHost.Application.Integrations;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Variables;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
public class IntegrationCapabilityValidatorTests
{
	[Test]
	public void A_well_formed_integration_produces_no_conflicts()
	{
		var integration = new FakeIntegration
		{
			Id = "app.macro-deck.obs",
			Actions = [new StubAction("set-scene"), new StubAction("start-recording")]
		};

		var conflicts = IntegrationCapabilityValidator.Validate(integration);

		Assert.That(conflicts, Is.Empty);
	}

	[Test]
	public void A_single_segment_owner_id_is_rejected_as_an_integration_conflict()
	{
		var integration = new FakeIntegration { Id = "spotify" };

		var conflicts = IntegrationCapabilityValidator.Validate(integration);

		Assert.That(conflicts, Has.Count.EqualTo(1));
		Assert.That(conflicts[0].CapabilityType, Is.EqualTo("Integration"));
	}

	[Test]
	public void Duplicate_action_ids_produce_one_conflict_naming_that_id()
	{
		var integration = new FakeIntegration
		{
			Id = "app.macro-deck.obs",
			Actions = [new StubAction("set-scene"), new StubAction("set-scene")]
		};

		var conflicts = IntegrationCapabilityValidator.Validate(integration);

		Assert.That(conflicts, Has.Count.EqualTo(1));
		Assert.Multiple(() =>
		{
			Assert.That(conflicts[0].CapabilityType, Is.EqualTo("Action"));
			Assert.That(conflicts[0].LocalId, Is.EqualTo("set-scene"));
		});
	}

	[Test]
	public void A_non_kebab_case_action_id_is_rejected()
	{
		var integration = new FakeIntegration
		{
			Id = "app.macro-deck.obs",
			Actions = [new StubAction("SetVolume")]
		};

		var conflicts = IntegrationCapabilityValidator.Validate(integration);

		Assert.That(conflicts, Has.Count.EqualTo(1));
		Assert.That(conflicts[0].CapabilityType, Is.EqualTo("Action"));
	}

	[Test]
	public void An_action_id_containing_the_qualified_separator_is_rejected()
	{
		// An author must not be able to submit a qualified id as a local one - the host derives it.
		var integration = new FakeIntegration
		{
			Id = "app.macro-deck.obs",
			Actions = [new StubAction("app.macro-deck.obs::set-scene")]
		};

		var conflicts = IntegrationCapabilityValidator.Validate(integration);

		Assert.That(conflicts, Has.Count.EqualTo(1));
		Assert.That(conflicts[0].CapabilityType, Is.EqualTo("Action"));
	}

	[Test]
	public void A_platform_restricted_action_still_counts_toward_id_uniqueness()
	{
		var otherPlatforms = MacroDeckPlatform.All & ~MacroDeckIntegrationAttribute.Current;
		var integration = new FakeIntegration
		{
			Id = "app.macro-deck.obs",
			Actions = [new StubAction("set-scene", otherPlatforms), new StubAction("set-scene")]
		};

		var conflicts = IntegrationCapabilityValidator.Validate(integration);

		Assert.That(conflicts, Has.Count.EqualTo(1));
		Assert.That(conflicts[0].CapabilityType, Is.EqualTo("Action"));
	}

	[Test]
	public void Duplicate_event_definition_ids_are_rejected()
	{
		var integration = new FakeEventIntegration("app.macro-deck.obs",
			new EventDefinition { Id = "scene-changed", Name = "Scene Changed" },
			new EventDefinition { Id = "scene-changed", Name = "Scene Changed Again" });

		var conflicts = IntegrationCapabilityValidator.Validate(integration);

		Assert.That(conflicts, Has.Count.EqualTo(1));
		Assert.That(conflicts[0].CapabilityType, Is.EqualTo("Event"));
	}

	[Test]
	public void Duplicate_variable_definition_ids_are_rejected()
	{
		var integration = new FakeVariableIntegration("app.macro-deck.spotify",
			VariableDefinition.Eager("current_track_a", VariableType.Text) with { Id = "current-track" },
			VariableDefinition.Eager("current_track_b", VariableType.Text) with { Id = "current-track" });

		var conflicts = IntegrationCapabilityValidator.Validate(integration);

		Assert.That(conflicts, Has.Count.EqualTo(1));
		Assert.That(conflicts[0].CapabilityType, Is.EqualTo("Variable"));
	}

	[Test]
	public void Template_declared_variables_are_never_validated_as_definition_ids()
	{
		var integration = new FakeVariableIntegration("app.macro-deck.twitch",
			VariableDefinition.Eager($"twitch_{VariableNameTemplate.Placeholder("account")}_is_connected",
				VariableType.Text));

		var conflicts = IntegrationCapabilityValidator.Validate(integration);

		Assert.That(conflicts, Is.Empty);
	}

	private sealed class StubAction : IActionDefinition
	{
		public StubAction(string id, MacroDeckPlatform platforms = MacroDeckPlatform.All)
		{
			Id = id;
			Platforms = platforms;
		}

		public string Id { get; }
		public LocalizedText Name => Id;
		public LocalizedText Description => string.Empty;
		public IReadOnlyList<ActionParameter> Parameters => [];
		public MacroDeckPlatform Platforms { get; }

		public IActionExecutor CreateExecutor() => throw new NotSupportedException();
	}

	private sealed class FakeEventIntegration : IIntegration, IEventProvider
	{
		public FakeEventIntegration(string id, params EventDefinition[] events)
		{
			Id = id;
			EventDefinitions = events;
		}

		public string Id { get; }
		public LocalizedText Name => Id;
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized => true;
		public string ProviderName => Id;
		public IReadOnlyList<EventDefinition> EventDefinitions { get; }

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}

	private sealed class FakeVariableIntegration : IIntegration, IVariableProvider
	{
		public FakeVariableIntegration(string id, params VariableDefinition[] declared)
		{
			Id = id;
			DeclaredVariables = declared;
		}

		public string Id { get; }
		public LocalizedText Name => Id;
		public string Version => "1.0.0";
		public IReadOnlyList<IActionDefinition> Actions => [];
		public bool IsInitialized => true;
		public IReadOnlyList<VariableDefinition> Variables => DeclaredVariables;
		public IReadOnlyList<VariableDefinition> DeclaredVariables { get; }

		public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
			=> ValueTask.FromResult(VariableReading.Unavailable);

		public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

		public Task ShutdownAsync() => Task.CompletedTask;
	}
}
