using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Variables;
using DomainVariableType = MacroDeckHost.Domain.Enums.VariableType;
using SdkVariableType = MacroDeck.Sdk.Variables.VariableType;
using VariableWriteCapability = MacroDeck.Sdk.Variables.VariableWriteCapability;

namespace MacroDeckHost.Tests.UnitTests.Ui;

/// <summary>
/// Before ADR 0081 this handler carried a rule of its own - an integration-owned variable could not be set
/// from the UI, on purpose - and that rule is what made a provider variable structurally read-only to every
/// client. It is gone: the handler parses, checks the lock, and hands the write to the one dispatch path,
/// which is the only thing that decides whether the write is allowed.
/// </summary>
[TestFixture]
public class SetVariableValueHandlerTests
{
	private const string IntegrationId = "provider";

	[Test]
	public async Task A_writable_provider_variable_is_accepted_and_reported_as_pending()
	{
		var (handler, provider, id) = Build(new VariableWriteCapability());

		var response = await handler.Handle(new SetVariableValueRequest { Id = id.ToString(), Value = "42" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(provider.Writes.Single().Value, Is.EqualTo(42m));

			// Pending, because the value the client now shows is the one it asked for, not one the provider
			// has confirmed: the authoritative reading arrives later on the read side.
			Assert.That(response.Pending, Is.True);
		});
	}

	[Test]
	public async Task A_provider_variable_that_declares_no_write_is_refused_in_words_a_person_can_read()
	{
		var (handler, provider, id) = Build(null);

		var response = await handler.Handle(new SetVariableValueRequest { Id = id.ToString(), Value = "42" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(VariableError.NotWritable)));
			Assert.That(response.Error.Message.Localized?.Key,
				Is.EqualTo(AppStrings.Errors.Variables.NotWritable().Key),
				"the refusal is a localized message for a person, not the service's diagnostic text");
			Assert.That(provider.Writes, Is.Empty);
		});
	}

	[Test]
	public async Task A_value_the_provider_rejects_is_refused_in_words_a_person_can_read()
	{
		var (handler, provider, id) = Build(new VariableWriteCapability());
		provider.Answer = VariableWriteResult.InvalidValue("volume out of range for device X");

		var response = await handler.Handle(new SetVariableValueRequest { Id = id.ToString(), Value = "42" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(VariableError.InvalidValue)));
			Assert.That(response.Error.Message.Localized?.Key,
				Is.EqualTo(AppStrings.Errors.Variables.ValueNotAccepted().Key));
		});
	}

	[Test]
	public async Task A_variable_the_provider_no_longer_knows_is_refused_in_words_a_person_can_read()
	{
		var (handler, provider, id) = Build(new VariableWriteCapability());
		provider.Answer = VariableWriteResult.NotFound("no such entity");

		var response = await handler.Handle(new SetVariableValueRequest { Id = id.ToString(), Value = "42" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(VariableError.NotFound)));
			Assert.That(response.Error.Message.Localized?.Key,
				Is.EqualTo(AppStrings.Errors.Variables.NotFound().Key));
		});
	}

	[Test]
	public async Task A_user_variable_is_applied_immediately_rather_than_left_pending()
	{
		var (handler, _, _, service, _) = BuildParts(new VariableWriteCapability());
		var created = await service.CreateUserVariable("greeting",
			VariableScope.Global,
			null,
			DomainVariableType.Text,
			"hello",
			null);

		var response = await handler.Handle(
			new SetVariableValueRequest { Id = created.Data!.Id.ToString(), Value = "bye" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(response.Pending, Is.False);
			Assert.That(response.Variable!.Value, Is.EqualTo("bye"));
		});
	}

	[Test]
	public async Task A_template_variable_is_refused_as_not_writable_in_words_a_person_can_read()
	{
		var (handler, _, _, service, _) = BuildParts(new VariableWriteCapability());
		var created = await service.CreateUserVariable("computed",
			VariableScope.Global,
			null,
			DomainVariableType.Text,
			null,
			null,
			templateSource: new VariableTemplateSource("fixed"));

		var response = await handler.Handle(
			new SetVariableValueRequest { Id = created.Data!.Id.ToString(), Value = "other" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(VariableError.NotWritable)));
			Assert.That(response.Error.Message.Localized?.Key,
				Is.EqualTo(AppStrings.Errors.Variables.TemplateReadOnly().Key));
		});
	}

	[Test]
	public async Task An_owner_that_reports_itself_unavailable_is_named_by_its_display_name()
	{
		var (handler, provider, id) = Build(new VariableWriteCapability());
		provider.Answer = VariableWriteResult.Unavailable("offline");

		var response = await handler.Handle(new SetVariableValueRequest { Id = id.ToString(), Value = "42" },
			CancellationToken.None);

		AssertOwnerUnavailable(response, "Volume Mixer");
	}

	[Test]
	public async Task A_disabled_built_in_owner_is_named_by_its_localized_name()
	{
		var parts = BuildParts(new VariableWriteCapability(), AppStrings.Integrations.System.Name());
		parts.Integrations.SetEnabled(IntegrationId, false);

		var response = await parts.Handler.Handle(
			new SetVariableValueRequest { Id = parts.Id.ToString(), Value = "42" },
			CancellationToken.None);

		AssertOwnerUnavailable(response, "System");
		Assert.That(parts.Provider.Writes, Is.Empty);
	}

	[Test]
	public async Task An_owner_that_is_no_longer_registered_is_named_by_its_id()
	{
		var parts = BuildParts(new VariableWriteCapability());
		await parts.Integrations.UnregisterAsync(IntegrationId);

		var response = await parts.Handler.Handle(
			new SetVariableValueRequest { Id = parts.Id.ToString(), Value = "42" },
			CancellationToken.None);

		AssertOwnerUnavailable(response, IntegrationId);
	}

	private static void AssertOwnerUnavailable(SetVariableValueResponse response, string integration)
	{
		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(nameof(VariableError.OwnerUnavailable)));
			Assert.That(response.Error.Message.Localized?.Key,
				Is.EqualTo(AppStrings.Errors.Variables.OwnerUnavailable(integration).Key));
			Assert.That(TestLocalization.Resolve(response.Error.Message),
				Is.EqualTo($"{integration} is not available right now"));
		});
	}

	private static (SetVariableValueRequestMessageHandler Handler, FakeWritableVariableProviderIntegration Provider,
		Guid Id) Build(VariableWriteCapability? write)
	{
		var parts = BuildParts(write);
		return (parts.Handler, parts.Provider, parts.Id);
	}

	private static (SetVariableValueRequestMessageHandler Handler, FakeWritableVariableProviderIntegration Provider,
		Guid Id, VariableService Service, ConfigurableIntegrationRegistry Integrations) BuildParts(
			VariableWriteCapability? write,
			LocalizedText? name = null)
	{
		var provider = new FakeWritableVariableProviderIntegration
		{
			Id = IntegrationId,
			Name = name ?? "Volume Mixer",
			IsInitialized = true,
			Variables =
			[
				VariableDefinition.Eager("volume", SdkVariableType.Numeric) with { Id = "volume", Write = write }
			]
		};

		var registry = new VariableRegistry();
		var integrations = new ConfigurableIntegrationRegistry([provider]);
		var service = TestVariableServices.Create(registry,
			new NullUserVariableStore(),
			new RecordingMediator(),
			integrations);

		var created = service.CreateIntegrationVariable(IntegrationId,
				"volume",
				VariableScope.Global,
				null,
				DomainVariableType.Numeric,
				10,
				0,
				"volume",
				new VariableDeclaration { Write = write })
			.GetAwaiter()
			.GetResult();

		var handler = new SetVariableValueRequestMessageHandler(service,
			new FakeHostLockState { IsLocked = false },
			registry,
			InMemorySharedVariableStore.For(registry),
			new VariableOwnerNames(integrations, TestLocalization.ScopeFactory));

		return (handler, provider, created.Data!.Id, service, integrations);
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load()
		{
			return [];
		}

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}
}
