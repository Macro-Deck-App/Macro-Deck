using System.Text.Json;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Variables.Colors;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
public class DeviceSurfaceColorContractTests
{
	[Test]
	public async Task A_provider_receives_resolved_opaque_colours()
	{
		var variables = new VariableRegistry();
		variables.Upsert(new VariableEntity
		{
			Id = Guid.NewGuid(),
			Name = "primary",
			Scope = VariableScope.Global,
			Type = VariableType.Color,
			Classification = VariableClassification.User,
			Value = "#3366ff"
		});

		var profiles = ContractDeck.Registry();
		profiles.GetProfiles().Single().DefaultBackgroundColor = "#10101080";
		profiles.GetFoldersForProfile(ContractDeck.ProfileId)
			.Single(folder => folder.Id == ContractDeck.FolderId)
			.Widgets.Single().Data = JsonSerializer.Serialize(new Dictionary<string, object?>
			{
				["label"] = "Record",
				["backgroundColor"] = "{{ vars.primary | color | color_opacity: 50 }}",
				["labelColor"] = "#11223344"
			});

		var projection = await ContractDeck.Builder(profiles, new ColorReferenceResolver(variables))
			.BuildAsync(ContractDeck.Device(Guid.NewGuid(), "integration.device", "SERIAL-1"),
				null,
				ContractDeck.FolderId,
				CancellationToken.None);

		var appearance = projection.Surface.Widgets
			.Single(widget => widget.Id == ContractDeck.WidgetId.ToString())
			.Appearance!;
		Assert.Multiple(() =>
		{
			Assert.That(appearance.BackgroundColor, Is.EqualTo("#3366ff"));
			Assert.That(appearance.LabelColor, Is.EqualTo("#112233"));
			Assert.That(projection.Surface.Layout!.BackgroundColor, Is.EqualTo("#101010"));
		});
	}

	[Test]
	public async Task A_reference_that_does_not_resolve_reaches_the_provider_as_no_colour()
	{
		var profiles = ContractDeck.Registry();
		profiles.GetFoldersForProfile(ContractDeck.ProfileId)
			.Single(folder => folder.Id == ContractDeck.FolderId)
			.Widgets.Single().Data = JsonSerializer.Serialize(new Dictionary<string, object?>
			{
				["label"] = "Record",
				["backgroundColor"] = "{{ vars.missing | color }}"
			});

		var projection = await ContractDeck.Builder(profiles, new ColorReferenceResolver(new VariableRegistry()))
			.BuildAsync(ContractDeck.Device(Guid.NewGuid(), "integration.device", "SERIAL-1"),
				null,
				ContractDeck.FolderId,
				CancellationToken.None);

		Assert.That(projection.Surface.Widgets.Single(widget => widget.Id == ContractDeck.WidgetId.ToString())
			.Appearance!.BackgroundColor, Is.Null);
	}

	[TestCase("rgb(1,2,3)")]
	[TestCase("#ABC")]
	[TestCase("red")]
	[TestCase("#3366ff")]
	public async Task A_colour_without_alpha_or_reference_reaches_the_provider_exactly_as_stored(string stored)
	{
		var profiles = ContractDeck.Registry();
		profiles.GetFoldersForProfile(ContractDeck.ProfileId)
			.Single(folder => folder.Id == ContractDeck.FolderId)
			.Widgets.Single().Data = JsonSerializer.Serialize(new Dictionary<string, object?>
			{
				["label"] = "Record",
				["backgroundColor"] = stored,
				["labelColor"] = stored
			});

		var projection = await ContractDeck.Builder(profiles, new ColorReferenceResolver(new VariableRegistry()))
			.BuildAsync(ContractDeck.Device(Guid.NewGuid(), "integration.device", "SERIAL-1"),
				null,
				ContractDeck.FolderId,
				CancellationToken.None);

		var appearance = projection.Surface.Widgets.Single(widget => widget.Id == ContractDeck.WidgetId.ToString())
			.Appearance!;
		Assert.Multiple(() =>
		{
			Assert.That(appearance.BackgroundColor, Is.EqualTo(stored));
			Assert.That(appearance.LabelColor, Is.EqualTo(stored));
		});
	}
}
