using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeck.Localization;
using MacroDeck.Sdk.Widgets;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Api;

[TestFixture]
public class WidgetSchemaEndpointTests
{
	private static readonly JsonSerializerOptions _mvcLikeOptions =
		new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

	private static WidgetsController CreateController(
		IUiTransportMessageHandler<GetWidgetDataSchemasRequest, GetWidgetDataSchemasResponse> handler)
		=> new(null!, null!, null!, null!, null!, null!, null!, null!, handler, null!, null!, null!, null!, null!);

	[Test]
	public async Task GetDataSchemas_serves_a_schema_for_every_built_in_type()
	{
		var provider = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator()));
		var controller = CreateController(new GetWidgetDataSchemasRequestMessageHandler(provider));

		var response = await controller.GetDataSchemas(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);

			foreach (var type in WidgetTypeIds.BuiltIn)
			{
				Assert.That(response.Schemas.TryGetValue(type, out var schema),
					Is.True,
					$"missing schema for {type}");
				Assert.That(schema.ValueKind, Is.EqualTo(JsonValueKind.Object));
			}
		});
	}

	[Test]
	public async Task GetDataSchemas_also_serves_a_provider_contributed_schema()
	{
		var registry = new WidgetTypeRegistry(new RecordingMediator());
		await registry.Register("com.example.gauges",
			new WidgetTypeDescriptor("gauge",
				LocalizedText.FromLiteral("Gauge"),
				HasConfiguration: true,
				DataSchema: """{"type":"object","properties":{"min":{"type":"number"}}}"""));
		var provider = new WidgetDataSchemaProvider(registry);
		var controller = CreateController(new GetWidgetDataSchemasRequestMessageHandler(provider));

		var response = await controller.GetDataSchemas(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Schemas.TryGetValue("com.example.gauges::gauge", out var schema), Is.True);
			Assert.That(schema.ValueKind, Is.EqualTo(JsonValueKind.Object));
			Assert.That(schema.GetProperty("properties").GetProperty("min").GetProperty("type").GetString(),
				Is.EqualTo("number"));

			// Every built-in must still be there alongside it - one provider's schema must not crowd out
			// or replace what Macro Deck itself ships.
			foreach (var type in WidgetTypeIds.BuiltIn)
			{
				Assert.That(response.Schemas.ContainsKey(type), Is.True, $"missing schema for {type}");
			}
		});
	}

	[Test]
	public async Task GetDataSchemas_serializes_raw_schema_keys_unchanged_by_the_camelCase_naming_policy()
	{
		var provider = new WidgetDataSchemaProvider(new WidgetTypeRegistry(new RecordingMediator()));
		var controller = CreateController(new GetWidgetDataSchemasRequestMessageHandler(provider));

		var response = await controller.GetDataSchemas(CancellationToken.None);

		var serialized = JsonSerializer.SerializeToNode(response, _mvcLikeOptions)!.AsObject();
		var schemasNode = serialized["schemas"]!.AsObject();

		foreach (var type in WidgetTypeIds.BuiltIn)
		{
			var original = provider.All()[type];
			var roundTripped = schemasNode[type];

			Assert.That(JsonNode.DeepEquals(JsonNode.Parse(original.GetRawText()), roundTripped),
				Is.True,
				$"{type} schema content changed across controller serialization");
		}

		var actionButton = schemasNode[WidgetTypeIds.ActionButton]!.AsObject();
		Assert.Multiple(() =>
		{
			Assert.That(actionButton.ContainsKey("$schema"), Is.True);
			Assert.That(actionButton.ContainsKey("$id"), Is.True);
		});
	}
}
