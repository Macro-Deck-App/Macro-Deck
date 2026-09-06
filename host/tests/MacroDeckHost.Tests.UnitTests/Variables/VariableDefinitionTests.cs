using MacroDeck.Localization;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Identity;

namespace MacroDeckHost.Tests.UnitTests.Variables;

[TestFixture]
public class VariableDefinitionTests
{
	private const string OwnerId = "app.macro-deck.spotify";

	private RecordingMediator _mediator = null!;
	private VariableRegistry _registry = null!;
	private VariableService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_mediator = new RecordingMediator();
		_registry = new VariableRegistry();
		_service = TestVariableServices.Create(_registry, new NullUserVariableStore(), _mediator);
	}

	[Test]
	public async Task An_omitted_definition_id_is_derived_from_the_canonical_name()
	{
		var result = await _service.CreateIntegrationVariable(OwnerId,
			"spotify_current_track",
			VariableScope.Global,
			null,
			VariableType.Text,
			null,
			null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.DefinitionId, Is.EqualTo("spotify-current-track"));
		});
	}

	[Test]
	public async Task An_explicit_definition_id_is_used_as_given()
	{
		var result = await _service.CreateIntegrationVariable(OwnerId,
			"spotify_track",
			VariableScope.Global,
			null,
			VariableType.Text,
			null,
			null,
			"now-playing");

		Assert.That(result.Data!.DefinitionId, Is.EqualTo("now-playing"));
	}

	[Test]
	public async Task Re_registering_the_same_definition_resolves_to_the_same_entity_and_runtime_id()
	{
		var first = await _service.CreateIntegrationVariable(OwnerId,
			"spotify_current_track",
			VariableScope.Global,
			null,
			VariableType.Text,
			null,
			null);

		var second = await _service.CreateIntegrationVariable(OwnerId,
			"spotify_current_track",
			VariableScope.Global,
			null,
			VariableType.Text,
			null,
			null);

		Assert.Multiple(() =>
		{
			Assert.That(second.Success, Is.True);
			Assert.That(second.Error, Is.Not.EqualTo(VariableError.AlreadyExists));
			Assert.That(second.Data!.Id, Is.EqualTo(first.Data!.Id));
			Assert.That(second.Data, Is.SameAs(first.Data));
		});
	}

	[Test]
	public async Task Re_registering_a_definition_under_a_new_name_renames_the_same_entity_and_indexes()
	{
		var first = await _service.CreateIntegrationVariable(OwnerId,
			"obs_home_current_scene",
			VariableScope.Global,
			null,
			VariableType.Text,
			null,
			null,
			"entry-abc-current-scene");

		var renamed = await _service.CreateIntegrationVariable(OwnerId,
			"obs_studio_current_scene",
			VariableScope.Global,
			null,
			VariableType.Text,
			null,
			null,
			"entry-abc-current-scene");

		Assert.Multiple(() =>
		{
			Assert.That(renamed.Success, Is.True);
			Assert.That(renamed.Data!.Id, Is.EqualTo(first.Data!.Id));
			Assert.That(_registry.FindByName(VariableScope.Global, null, "obs_home_current_scene"), Is.Null);
			Assert.That(_registry.FindByName(VariableScope.Global, null, "obs_studio_current_scene"),
				Is.SameAs(first.Data));
		});
	}

	[Test]
	public async Task Re_registering_the_same_definition_under_a_renamed_configuration_updates_the_label()
	{
		var first = await _service.CreateIntegrationVariable(OwnerId,
			"twitch_streamer_is_live",
			VariableScope.Global,
			null,
			VariableType.Boolean,
			null,
			null,
			"twitch-streamer-is-live",
			new VariableDeclaration
			{
				Presentation = new VariablePresentation(LocalizedText.FromLiteral("Is live"),
					"streamer",
					LocalizedText.FromLiteral("Streamer"))
			});

		var renamed = await _service.CreateIntegrationVariable(OwnerId,
			"twitch_streamer_is_live",
			VariableScope.Global,
			null,
			VariableType.Boolean,
			null,
			null,
			"twitch-streamer-is-live",
			new VariableDeclaration
			{
				Presentation = new VariablePresentation(LocalizedText.FromLiteral("Is live"),
					"streamer",
					LocalizedText.FromLiteral("Main Channel"))
			});

		Assert.Multiple(() =>
		{
			Assert.That(renamed.Success, Is.True);
			Assert.That(renamed.Data!.Id, Is.EqualTo(first.Data!.Id));
			Assert.That(renamed.Data!.Presentation!.ConfigurationName.Literal, Is.EqualTo("Main Channel"));
			Assert.That(first.Data!.Presentation!.ConfigurationName.Literal,
				Is.EqualTo("Main Channel"),
				"the same entity instance is returned, so its label reflects the rename too");
		});
	}

	[Test]
	public async Task A_second_declaration_reaching_the_same_definition_id_is_rejected()
	{
		await _service.CreateIntegrationVariable(OwnerId,
			"spotify_current_track",
			VariableScope.Global,
			null,
			VariableType.Text,
			null,
			null,
			"current-track");

		var second = await _service.CreateIntegrationVariable(OwnerId,
			"spotify_now_playing",
			VariableScope.Global,
			null,
			VariableType.Numeric,
			null,
			null,
			"current-track");

		Assert.Multiple(() =>
		{
			Assert.That(second.Success, Is.False);
			Assert.That(second.Error, Is.EqualTo(VariableError.AlreadyExists));
			Assert.That(second.ErrorMessage, Does.Contain("current-track"));
		});
	}

	[Test]
	public async Task GetByDefinition_resolves_the_variable_by_its_qualified_definition_id()
	{
		var created = await _service.CreateIntegrationVariable(OwnerId,
			"spotify_current_track",
			VariableScope.Global,
			null,
			VariableType.Text,
			null,
			null);

		var resolved = await _service.GetByDefinition(QualifiedId.Create(OwnerId, "spotify-current-track"));

		Assert.That(resolved, Is.SameAs(created.Data));
	}

	[Test]
	public async Task An_explicit_definition_id_that_is_not_valid_declared_kebab_case_is_rejected()
	{
		var result = await _service.CreateIntegrationVariable(OwnerId,
			"spotify_track",
			VariableScope.Global,
			null,
			VariableType.Text,
			null,
			null,
			"Not Kebab");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(VariableError.InvalidName));
		});
	}

	[Test]
	public async Task A_user_variable_has_no_definition_id()
	{
		var result = await _service.CreateUserVariable("my_variable",
			VariableScope.Global,
			null,
			VariableType.Text,
			"hello",
			null);

		Assert.That(result.Data!.DefinitionId, Is.Null);
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}
}
