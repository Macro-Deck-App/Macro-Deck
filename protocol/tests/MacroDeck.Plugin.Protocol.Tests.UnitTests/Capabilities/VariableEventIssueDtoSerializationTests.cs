using System.Reflection;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.Events;
using MacroDeck.Plugin.Protocol.Capabilities.Issues;
using MacroDeck.Plugin.Protocol.Capabilities.Variables;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

/// <summary>Round-trips every DTO added for the <c>variables</c>, <c>events</c> and <c>issues</c>
/// capability kinds through the wire serializer, in the style of <see cref="CapabilityDtoSerializationTests" />.</summary>
[TestFixture]
public class VariableEventIssueDtoSerializationTests
{
	[Test]
	public void Variable_catalog_payload_round_trips_and_tolerates_unknown_fields()
	{
		var payload = new VariableCatalogPayload
		{
			DeclaredVariables =
			[
				new VariableDefinitionDto
					{ Name = "cpu_temp", Type = "Numeric", DecimalPlaces = 1, Id = "cpu-temp" }
			],
			Variables =
			[
				new VariableDefinitionDto
				{
					Name = "cpu_temp", Type = "Numeric", DecimalPlaces = 1, RefreshIntervalSeconds = 2.5,
					Id = "cpu-temp"
				}
			],
			VariablesDependOnConfiguration = true
		};

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<VariableCatalogPayload>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.DeclaredVariables, Has.Count.EqualTo(1));
			Assert.That(actual.DeclaredVariables[0].Name, Is.EqualTo("cpu_temp"));
			Assert.That(actual.DeclaredVariables[0].Type, Is.EqualTo("Numeric"));
			Assert.That(actual.Variables[0].RefreshIntervalSeconds, Is.EqualTo(2.5));
			Assert.That(actual.VariablesDependOnConfiguration, Is.True);
		});
	}

	[Test]
	public void Variable_descriptor_round_trips_the_display_name_and_configuration_fields()
	{
		var dto = new VariableDefinitionDto
		{
			Name = "current_scene",
			Type = "Text",
			Id = "current-scene",
			DisplayName = "Current scene",
			ConfigurationKey = "obs-main",
			ConfigurationName = "Main OBS"
		};

		var json = JsonSerializer.Serialize(dto, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<VariableDefinitionDto>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.DisplayName?.Literal, Is.EqualTo("Current scene"));
			Assert.That(actual.ConfigurationKey, Is.EqualTo("obs-main"));
			Assert.That(actual.ConfigurationName?.Literal, Is.EqualTo("Main OBS"));
		});
	}

	/// <summary>An old plugin/host, built before <see cref="VariableDefinitionDto.DisplayName" />,
	/// <see cref="VariableDefinitionDto.ConfigurationKey" /> and
	/// <see cref="VariableDefinitionDto.ConfigurationName" /> existed, never emits those members. A
	/// current reader must still deserialize such a payload without error, leaving the new
	/// properties null and every pre-existing field unchanged - the additive-compatibility
	/// contract from CLAUDE.md's "Public SDK and plugin compatibility" section.
	///
	/// <para>
	/// The one member that does not survive is the id: <c>variables</c> capability version 1 called it
	/// <c>definitionId</c> and version 2 calls it <c>id</c>, so a version 1 payload read through this type
	/// leaves <see cref="VariableDefinitionDto.Id" /> null rather than silently re-keying the variable.
	/// That is why anything that has to read a stored version 1 document - the remote plugin snapshot
	/// store - models the old shape separately instead of reusing this DTO.
	/// </para>
	/// </summary>
	[Test]
	public void Variable_descriptor_from_a_pre_display_name_producer_deserializes_with_the_new_fields_null()
	{
		const string oldProducerJson =
			"""
			{"name":"cpu_temp","type":"Numeric","decimalPlaces":1,"refreshIntervalSeconds":2.5,"definitionId":"cpu-temp"}
			""";

		var actual = JsonSerializer.Deserialize<VariableDefinitionDto>(oldProducerJson, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.Name, Is.EqualTo("cpu_temp"));
			Assert.That(actual.Type, Is.EqualTo("Numeric"));
			Assert.That(actual.DecimalPlaces, Is.EqualTo(1));
			Assert.That(actual.RefreshIntervalSeconds, Is.EqualTo(2.5));
			Assert.That(actual.Id, Is.Null);
			Assert.That(actual.Materialization, Is.EqualTo(VariableMaterializations.Eager));
			Assert.That(actual.DisplayName, Is.Null);
			Assert.That(actual.ConfigurationKey, Is.Null);
			Assert.That(actual.ConfigurationName, Is.Null);
		});
	}

	[Test]
	public void Variable_value_dto_round_trips_each_tagged_case()
	{
		AssertRoundTrips(new VariableValueDto { Kind = "text", Text = "hello" });
		AssertRoundTrips(new VariableValueDto { Kind = "number", Number = 42.5 });
		AssertRoundTrips(new VariableValueDto { Kind = "boolean", Boolean = true });
		AssertRoundTrips(VariableValueDto.Unavailable);

		return;

		static void AssertRoundTrips(VariableValueDto value)
		{
			var json = JsonSerializer.Serialize(value, PluginProtocolJson.Options);
			var actual = JsonSerializer.Deserialize<VariableValueDto>(json, PluginProtocolJson.Options);

			Assert.That(actual, Is.EqualTo(value));
		}
	}

	[Test]
	public void Event_catalog_payload_round_trips_and_tolerates_unknown_fields()
	{
		var payload = new EventCatalogPayload
		{
			ProviderName = "OBS Studio",
			Events =
			[
				new EventDescriptorDto
				{
					LocalId = "scene-changed",
					Name = "Scene changed",
					Description = "Fires when the active scene changes.",
					Category = "Scenes",
					IconName = "scene",
					DeliveryKind = "Push",
					ConfigurationParameters = [new ActionParameterDto { Name = "scene", Type = "String" }],
					PayloadParameters = [new ActionParameterDto { Name = "sceneName", Type = "String" }]
				}
			],
			HasDynamicEventOptions = true
		};

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<EventCatalogPayload>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.ProviderName, Is.EqualTo("OBS Studio"));
			Assert.That(actual.Events, Has.Count.EqualTo(1));
			Assert.That(actual.Events[0].LocalId, Is.EqualTo("scene-changed"));
			Assert.That(actual.Events[0].DeliveryKind, Is.EqualTo("Push"));
			Assert.That(actual.Events[0].ConfigurationParameters, Has.Count.EqualTo(1));
			Assert.That(actual.Events[0].PayloadParameters, Has.Count.EqualTo(1));
			Assert.That(actual.HasDynamicEventOptions, Is.True);
		});
	}

	[Test]
	public void Event_options_arguments_round_trip_and_tolerate_unknown_fields()
	{
		var arguments = new EventOptionsArguments
		{
			EventId = "scene-changed",
			ParameterName = "scene",
			Filter = "liv",
			CurrentParameters = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				["other"] = JsonDocument.Parse("\"value\"").RootElement
			}
		};

		var json = JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<EventOptionsArguments>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.EventId, Is.EqualTo("scene-changed"));
			Assert.That(actual.ParameterName, Is.EqualTo("scene"));
			Assert.That(actual.Filter, Is.EqualTo("liv"));
			Assert.That(actual.CurrentParameters, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public void Integration_issue_descriptor_round_trips_and_tolerates_unknown_fields()
	{
		var dto = new IntegrationIssueDescriptorDto
		{
			Id = "not-connected",
			Title = "Not connected",
			Description = "The service is unreachable.",
			Severity = "Error",
			ActionLabel = "Reconnect"
		};

		var json = JsonSerializer.Serialize(dto, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<IntegrationIssueDescriptorDto>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.Id, Is.EqualTo("not-connected"));
			Assert.That(actual.Severity, Is.EqualTo("Error"));
			Assert.That(actual.ActionLabel?.Literal, Is.EqualTo("Reconnect"));
		});
	}

	[Test]
	public void Issue_list_result_round_trips_and_tolerates_unknown_fields()
	{
		var result = new IssueListResult
		{
			Issues = [new IntegrationIssueDescriptorDto { Id = "a", Title = "A", Severity = "Warning" }]
		};

		var json = JsonSerializer.Serialize(result, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<IssueListResult>(InjectUnknownField(json), PluginProtocolJson.Options);

		Assert.That(actual!.Issues, Has.Count.EqualTo(1));
	}

	[Test]
	public void Issue_resolve_arguments_and_result_round_trip_and_tolerate_unknown_fields()
	{
		var arguments = new IssueResolveArguments { IssueId = "not-connected" };
		var argumentsJson = JsonSerializer.Serialize(arguments, PluginProtocolJson.Options);
		var actualArguments =
			JsonSerializer.Deserialize<IssueResolveArguments>(InjectUnknownField(argumentsJson),
				PluginProtocolJson.Options);
		Assert.That(actualArguments!.IssueId, Is.EqualTo("not-connected"));

		var result = new IssueResolveResult { Success = true, Message = "Reconnected.", FollowUp = "None" };
		var resultJson = JsonSerializer.Serialize(result, PluginProtocolJson.Options);
		var actualResult
			= JsonSerializer.Deserialize<IssueResolveResult>(InjectUnknownField(resultJson),
				PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(actualResult!.Success, Is.True);
			Assert.That(actualResult.Message?.Literal, Is.EqualTo("Reconnected."));
			Assert.That(actualResult.FollowUp, Is.EqualTo("None"));
		});
	}

	/// <summary>Same guard as <see cref="CapabilityDtoSerializationTests" />'s, extended to the three
	/// new namespaces: nothing here may carry an enum-typed property, since <see cref="PluginProtocolJson.Options" />
	/// has no <see cref="System.Text.Json.Serialization.JsonStringEnumConverter" />.</summary>
	[Test]
	public void No_dto_in_variables_events_or_issues_exposes_an_enum_typed_property()
	{
		var candidateTypes = typeof(VariableDefinitionDto).Assembly.GetTypes()
			.Where(type =>
				type.IsPublic &&
				type.Namespace is not null &&
				(type.Namespace == typeof(VariableDefinitionDto).Namespace ||
					type.Namespace == typeof(EventDescriptorDto).Namespace ||
					type.Namespace == typeof(IntegrationIssueDescriptorDto).Namespace));

		Assert.Multiple(() =>
		{
			foreach (var type in candidateTypes)
			{
				foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
				{
					Assert.That(property.PropertyType.IsEnum,
						Is.False,
						$"{type.FullName}.{property.Name} is an enum, which would serialize as an undocumented integer.");
				}
			}
		});
	}

	private static string InjectUnknownField(string json)
	{
		var body = json[..^1];
		var separator = body.EndsWith('{') ? string.Empty : ",";
		return body + separator + "\"unknownField\":\"ignored\"}";
	}
}
