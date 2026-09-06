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
		var (handler, provider, id) = Build(write: null);

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
	public async Task A_user_variable_is_applied_immediately_rather_than_left_pending()
	{
		var (handler, _, _, service) = BuildParts(new VariableWriteCapability());
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

	private static (SetVariableValueRequestMessageHandler Handler, FakeWritableVariableProviderIntegration Provider,
		Guid Id) Build(VariableWriteCapability? write)
	{
		var (handler, provider, id, _) = BuildParts(write);
		return (handler, provider, id);
	}

	private static (SetVariableValueRequestMessageHandler Handler, FakeWritableVariableProviderIntegration Provider,
		Guid Id, VariableService Service) BuildParts(VariableWriteCapability? write)
	{
		var provider = new FakeWritableVariableProviderIntegration
		{
			Id = IntegrationId,
			IsInitialized = true,
			Variables =
			[
				VariableDefinition.Eager("volume", SdkVariableType.Numeric) with { Id = "volume", Write = write }
			]
		};

		var service = TestVariableServices.Create(new VariableRegistry(),
			new NullUserVariableStore(),
			new RecordingMediator(),
			new ConfigurableIntegrationRegistry([provider]));

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

		var handler = new SetVariableValueRequestMessageHandler(service, new FakeHostLockState { IsLocked = false });

		return (handler, provider, created.Data!.Id, service);
	}

	private sealed class NullUserVariableStore : IUserVariableStore
	{
		public IReadOnlyList<VariableEntity> Load() => [];

		public void Save(IEnumerable<VariableEntity> userVariables)
		{
		}
	}
}
