using MacroDeck.Sdk.Variables;
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

[TestFixture]
public class UpdateVariableHandlerTests
{
	private const string IntegrationId = "app.example.mixer";

	[Test]
	public async Task A_value_refused_by_an_unavailable_owner_names_the_owner_by_its_display_name()
	{
		var provider = new FakeWritableVariableProviderIntegration
		{
			Id = IntegrationId,
			Name = "Volume Mixer",
			IsInitialized = true,
			Answer = VariableWriteResult.Unavailable("offline"),
			Variables =
			[
				VariableDefinition.Eager("volume", SdkVariableType.Numeric) with
				{
					Id = "volume", Write = new VariableWriteCapability()
				}
			]
		};
		var registry = new VariableRegistry();
		var integrations = new ConfigurableIntegrationRegistry([provider]);
		var service = TestVariableServices.Create(registry,
			new NullUserVariableStore(),
			new RecordingMediator(),
			integrations);
		var created = await service.CreateIntegrationVariable(IntegrationId,
			"volume",
			VariableScope.Global,
			null,
			DomainVariableType.Numeric,
			10m,
			0,
			"volume",
			new VariableDeclaration { Write = new VariableWriteCapability() });
		var handler = new UpdateVariableRequestMessageHandler(service,
			new FakeHostLockState { IsLocked = false },
			registry,
			InMemorySharedVariableStore.For(registry),
			new VariableOwnerNames(integrations, TestLocalization.ScopeFactory));

		var response = await handler.Handle(
			new UpdateVariableRequest { Id = created.Data!.Id.ToString(), Value = "42" },
			CancellationToken.None);

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
}
