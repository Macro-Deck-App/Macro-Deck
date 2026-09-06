using System.Text.Json;
using MacroDeck.Localization.Serialization;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Events;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class GetEventDefinitionsRequestMessageHandlerTests
{
	private static readonly string[] _variableChangedPayload = ["variable", "value", "previousValue"];

	private static async Task<GetEventDefinitionsResponse> Handle(params IHostEventProvider[] hostProviders)
	{
		var registry = new EventRegistry(new FakeIntegrationRegistry(),
			hostProviders,
			new LoggerConfiguration().CreateLogger());
		var handler = new GetEventDefinitionsRequestMessageHandler(registry);
		return await handler.Handle(new GetEventDefinitionsRequest(), CancellationToken.None);
	}

	[Test]
	public async Task The_catalogue_carries_qualified_ids_and_the_provider_name()
	{
		var response = await Handle(new CoreEventProvider());

		var variableChanged = response.Events.First(e => e.Id == "macro-deck::variable-changed");

		Assert.Multiple(() =>
		{
			Assert.That(variableChanged.ProviderId, Is.EqualTo("macro-deck"));
			Assert.That(TestLocalization.Resolve(variableChanged.ProviderName), Is.EqualTo("Macro Deck"));
			Assert.That(variableChanged.IsIntegration, Is.False);
			Assert.That(TestLocalization.Resolve(variableChanged.Name), Is.EqualTo("Variable Changed"));
			Assert.That(variableChanged.DeliveryKind, Is.EqualTo("push"));
		});
	}

	[Test]
	public async Task Configuration_and_payload_parameters_are_mapped_separately()
	{
		var response = await Handle(new CoreEventProvider());

		var variableChanged = response.Events.First(e => e.Id == "macro-deck::variable-changed");

		Assert.Multiple(() =>
		{
			Assert.That(variableChanged.ConfigurationParameters.Select(p => p.Name), Does.Contain("variable"));
			Assert.That(variableChanged.ConfigurationParameters.First(p => p.Name == "variable").Required, Is.True);
			Assert.That(variableChanged.PayloadParameters.Select(p => p.Name),
				Is.EquivalentTo(_variableChangedPayload));
		});
	}

	/// <remarks>
	/// The host sends the reference, never the resolved text: a client resolves it in its own language,
	/// so switching language re-renders what the client already holds instead of refetching the catalogue.
	/// Resolving here would look correct in one language and freeze every other one.
	/// </remarks>
	[Test]
	public async Task The_catalogue_ships_localization_references_rather_than_resolved_text()
	{
		var response = await Handle(new CoreEventProvider());

		var variableChanged = response.Events.First(e => e.Id == "macro-deck::variable-changed");

		Assert.Multiple(() =>
		{
			Assert.That(variableChanged.ProviderName.IsLocalized, Is.True);
			Assert.That(variableChanged.Name.IsLocalized, Is.True);
			Assert.That(variableChanged.Category.IsLocalized, Is.True);
			Assert.That(JsonSerializer.Serialize(variableChanged.Name),
				Does.Contain(LocalizedTextJsonConverter.Marker));
		});
	}

	[Test]
	public async Task An_empty_catalogue_is_not_an_error()
	{
		var response = await Handle();

		Assert.That(response.Events, Is.Empty);
	}
}
