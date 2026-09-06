using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

/// <summary>Round-trips the catalog, read and write DTOs of the <c>variables</c> capability kind through
/// the wire serializer. The declaration and value halves live in
/// <see cref="VariableEventIssueDtoSerializationTests" />, which also carries the enum guard for the
/// whole namespace.</summary>
[TestFixture]
public class VariableCatalogDtoSerializationTests
{
	[Test]
	public void Catalog_payload_round_trips_the_catalog_capability_flags()
	{
		var payload = new VariableCatalogPayload
		{
			SupportsCatalog = true, SupportsPush = true, SupportsSearch = true, CatalogName = "Home Assistant"
		};

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<VariableCatalogPayload>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.CatalogName, Is.EqualTo("Home Assistant"));
			Assert.That(actual.SupportsCatalog, Is.True);
			Assert.That(actual.SupportsPush, Is.True);
			Assert.That(actual.SupportsSearch, Is.True);
		});
	}

	[Test]
	public void Definition_round_trips_its_catalog_attribute_and_write_fields()
	{
		var dto = new VariableDefinitionDto
		{
			Id = "sensor.office_temperature",
			Name = "office_temperature",
			Type = "Numeric",
			Materialization = VariableMaterializations.OnDemand,
			ParentId = "sensors",
			IsContainer = false,
			IsBindable = true,
			Description = "Office temperature sensor.",
			Icon = "thermometer",
			DecimalPlaces = 1,
			RefreshIntervalSeconds = 30,
			Unit = "°C",
			SemanticKind = "temperature",
			Attributes = new Dictionary<string, string> { ["friendly_name"] = "Office temperature" },
			Write = new VariableWriteCapabilityDto { CommitOnRelease = true }
		};

		var json = JsonSerializer.Serialize(dto, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<VariableDefinitionDto>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.Id, Is.EqualTo("sensor.office_temperature"));
			Assert.That(actual.Name, Is.EqualTo("office_temperature"));
			Assert.That(actual.Type, Is.EqualTo("Numeric"));
			Assert.That(actual.Materialization, Is.EqualTo(VariableMaterializations.OnDemand));
			Assert.That(actual.ParentId, Is.EqualTo("sensors"));
			Assert.That(actual.Description?.Literal, Is.EqualTo("Office temperature sensor."));
			Assert.That(actual.RefreshIntervalSeconds, Is.EqualTo(30));
			Assert.That(actual.Unit, Is.EqualTo("°C"));
			Assert.That(actual.SemanticKind, Is.EqualTo("temperature"));
			Assert.That(actual.Attributes?["friendly_name"], Is.EqualTo("Office temperature"));
			Assert.That(actual.Write?.CommitOnRelease, Is.True);
		});
	}

	/// <summary>A definition serialized before the materialization policy existed carries no
	/// <c>materialization</c> field, and has to deserialize as the eager kind that was the only one such a
	/// producer could describe.</summary>
	[Test]
	public void Definition_without_a_materialization_field_deserializes_as_eager()
	{
		const string json = """{"name":"cpu_temp","type":"Numeric"}""";

		var actual = JsonSerializer.Deserialize<VariableDefinitionDto>(json, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.Materialization, Is.EqualTo(VariableMaterializations.Eager));
			Assert.That(actual.Write, Is.Null);
			Assert.That(actual.IsBindable, Is.True);
		});
	}

	[Test]
	public void Discover_arguments_and_page_round_trip_and_tolerate_unknown_fields()
	{
		var arguments = new VariableDiscoverArguments
		{
			ParentId = "sensors", Search = "office", ContinuationToken = "page-2", PageSize = 25
		};

		var argumentsJson = JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);
		var actualArguments = JsonSerializer.Deserialize<VariableDiscoverArguments>(InjectUnknownField(argumentsJson),
			PluginProtocolJson.Options);

		Assert.That(actualArguments, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actualArguments!.ParentId, Is.EqualTo("sensors"));
			Assert.That(actualArguments.Search, Is.EqualTo("office"));
			Assert.That(actualArguments.ContinuationToken, Is.EqualTo("page-2"));
			Assert.That(actualArguments.PageSize, Is.EqualTo(25));
		});

		var page = new VariableCatalogPageResult
		{
			Items =
			[
				new VariableDefinitionDto
				{
					Id = "a",
					Name = "A",
					Type = "Text",
					Materialization = VariableMaterializations.OnDemand,
					IsContainer = true
				}
			],
			ContinuationToken = "next-page"
		};

		var pageJson = JsonSerializer.Serialize(page, PluginProtocolJson.Options);
		var actualPage = JsonSerializer.Deserialize<VariableCatalogPageResult>(InjectUnknownField(pageJson),
			PluginProtocolJson.Options);

		Assert.That(actualPage, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actualPage!.Items, Has.Count.EqualTo(1));
			Assert.That(actualPage.Items[0].IsContainer, Is.True);
			Assert.That(actualPage.ContinuationToken, Is.EqualTo("next-page"));
		});
	}

	/// <summary>The wrapper is what lets "not resolvable" (a present body with a null
	/// <see cref="VariableResolveResult.Definition" />) be distinguished from an absent result body, which
	/// the invoke plumbing already uses to mean "no data".</summary>
	[Test]
	public void Resolve_result_with_null_definition_round_trips_distinguishably_from_an_absent_body()
	{
		var result = new VariableResolveResult { Definition = null };

		var json = JsonSerializer.Serialize(result, PluginProtocolJson.Options);

		Assert.That(json, Does.Contain("\"definition\":null"));

		var actual = JsonSerializer.Deserialize<VariableResolveResult>(json, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.That(actual!.Definition, Is.Null);

		var resolvedResult = new VariableResolveResult
		{
			Definition = new VariableDefinitionDto
			{
				Id = "a", Name = "A", Type = "Text", Materialization = VariableMaterializations.OnDemand
			}
		};
		var resolvedJson = JsonSerializer.Serialize(resolvedResult, PluginProtocolJson.Options);
		var actualResolved = JsonSerializer.Deserialize<VariableResolveResult>(resolvedJson,
			PluginProtocolJson.Options);

		Assert.That(actualResolved!.Definition, Is.Not.Null);
	}

	[Test]
	public void Subscribe_arguments_and_result_round_trip_and_tolerate_unknown_fields()
	{
		var arguments = new VariableSubscribeArguments { Ids = ["a", "b"] };
		var argumentsJson = JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);
		var actualArguments = JsonSerializer.Deserialize<VariableSubscribeArguments>(InjectUnknownField(argumentsJson),
			PluginProtocolJson.Options);
		Assert.That(actualArguments!.Ids, Has.Count.EqualTo(2));
		Assert.That(actualArguments.Ids, Is.EquivalentTo(new List<string> { "a", "b" }));

		var result = new VariableSubscribeResult
		{
			Values =
			[
				new VariableIdValueDto
				{
					Id = "a",
					Reading = new VariableReadingDto
					{
						Value = new VariableValueDto { Kind = "text", Text = "hi" }
					}
				}
			]
		};
		var resultJson = JsonSerializer.Serialize(result, PluginProtocolJson.Options);
		var actualResult = JsonSerializer.Deserialize<VariableSubscribeResult>(InjectUnknownField(resultJson),
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(actualResult!.Values, Has.Count.EqualTo(1));
			Assert.That(actualResult.Values[0].Id, Is.EqualTo("a"));
			Assert.That(actualResult.Values[0].Reading.Value.Kind, Is.EqualTo("text"));
			Assert.That(actualResult.Values[0].Reading.Value.Text, Is.EqualTo("hi"));
		});
	}

	/// <summary>Covers every <see cref="VariableValueDto" /> tagged-union case as it is reused through
	/// <see cref="VariableIdValueDto.Reading" />, including the unavailable degrade case, and the volatile
	/// attributes that travel with a reading rather than with the declaration.</summary>
	[Test]
	public void Id_value_dto_round_trips_each_tagged_case_and_the_volatile_attributes()
	{
		AssertRoundTrips(new VariableIdValueDto
		{
			Id = "a",
			Reading = new VariableReadingDto { Value = new VariableValueDto { Kind = "text", Text = "hello" } }
		});
		AssertRoundTrips(new VariableIdValueDto
		{
			Id = "a",
			Reading = new VariableReadingDto
			{
				Value = new VariableValueDto { Kind = "number", Number = 42.5 }, Min = 0, Max = 120, Step = 0.5
			}
		});
		AssertRoundTrips(new VariableIdValueDto
		{
			Id = "a",
			Reading = new VariableReadingDto { Value = new VariableValueDto { Kind = "boolean", Boolean = true } }
		});
		AssertRoundTrips(new VariableIdValueDto
			{ Id = "a", Reading = new VariableReadingDto { Value = VariableValueDto.Unavailable } });

		return;

		static void AssertRoundTrips(VariableIdValueDto value)
		{
			var json = JsonSerializer.Serialize(value, PluginProtocolJson.Options);
			var actual = JsonSerializer.Deserialize<VariableIdValueDto>(json, PluginProtocolJson.Options);

			Assert.That(actual, Is.EqualTo(value));
		}
	}

	[Test]
	public void Set_arguments_and_result_round_trip_and_tolerate_unknown_fields()
	{
		var arguments = new VariableSetArguments { Value = new VariableValueDto { Kind = "number", Number = 62.5 } };
		var argumentsJson = JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);
		var actualArguments = JsonSerializer.Deserialize<VariableSetArguments>(InjectUnknownField(argumentsJson),
			PluginProtocolJson.Options);

		Assert.That(actualArguments, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actualArguments!.Value.Kind, Is.EqualTo("number"));
			Assert.That(actualArguments.Value.Number, Is.EqualTo(62.5));
		});

		var result = new VariableSetResult { Status = "NotWritable", Message = "Read-only entity." };
		var resultJson = JsonSerializer.Serialize(result, PluginProtocolJson.Options);
		var actualResult = JsonSerializer.Deserialize<VariableSetResult>(InjectUnknownField(resultJson),
			PluginProtocolJson.Options);

		Assert.That(actualResult, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actualResult!.Status, Is.EqualTo("NotWritable"));
			Assert.That(actualResult.Message?.Literal, Is.EqualTo("Read-only entity."));
		});
	}

	private static string InjectUnknownField(string json)
	{
		var body = json[..^1];
		var separator = body.EndsWith('{') ? string.Empty : ",";
		return body + separator + "\"unknownField\":\"ignored\"}";
	}
}
