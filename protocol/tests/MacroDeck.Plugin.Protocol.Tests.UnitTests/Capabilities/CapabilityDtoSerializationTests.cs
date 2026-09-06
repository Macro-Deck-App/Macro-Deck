using System.Reflection;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Actions;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

/// <summary>Round-trips every DTO added for callback and asset payloads through the wire serializer,
/// and guards the enum-as-integer trap those payloads are especially exposed to since several of
/// them (kind, api, operation) look like they want to be enums.</summary>
[TestFixture]
public class CapabilityDtoSerializationTests
{
	[Test]
	public void Host_invoke_payload_round_trips_with_camel_case_keys_and_tolerates_unknown_fields()
	{
		var payload = new HostInvokePayload { Api = HostApis.Variables, Operation = HostOperations.Variables.Get };

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(json, Does.Contain("\"api\""));
			Assert.That(json, Does.Contain("\"operation\""));
		});

		var withExtraField = InjectUnknownField(json);
		var actual = JsonSerializer.Deserialize<HostInvokePayload>(withExtraField, PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.Api, Is.EqualTo(payload.Api));
			Assert.That(actual.Operation, Is.EqualTo(payload.Operation));
		});
	}

	[Test]
	public void Host_result_payload_round_trips_and_tolerates_unknown_fields()
	{
		var payload = new HostResultPayload { Data = JsonDocument.Parse("{\"value\":1}").RootElement };

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<HostResultPayload>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.That(actual!.Data, Is.Not.Null);
	}

	[Test]
	public void Host_cancel_payload_reason_is_optional_and_tolerates_unknown_fields()
	{
		var withoutReason = new HostCancelPayload();
		var json = JsonSerializer.Serialize(withoutReason, PluginProtocolJson.Options);
		Assert.That(json, Does.Not.Contain("reason"));

		var payload = new HostCancelPayload { Reason = "user cancelled" };
		var roundTripped = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<HostCancelPayload>(InjectUnknownField(roundTripped),
			PluginProtocolJson.Options);

		Assert.That(actual!.Reason, Is.EqualTo(payload.Reason));
	}

	[Test]
	public void Host_state_payload_round_trips_and_tolerates_unknown_fields()
	{
		var payload = new HostStatePayload { Api = HostApis.Config, Data = JsonDocument.Parse("[1,2]").RootElement };

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<HostStatePayload>(InjectUnknownField(json), PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.Api, Is.EqualTo(payload.Api));
			Assert.That(actual.Data, Is.Not.Null);
		});
	}

	[Test]
	public void State_update_payload_round_trips_with_optional_fields_omitted_when_absent()
	{
		var minimal = new StateUpdatePayload { Kind = "actions" };
		var minimalJson = JsonSerializer.Serialize(minimal, PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(minimalJson, Does.Not.Contain("localId"));
			Assert.That(minimalJson, Does.Not.Contain("reason"));
		});

		var full = new StateUpdatePayload { Kind = "actions", LocalId = "set-volume", Reason = "config changed" };
		var json = JsonSerializer.Serialize(full, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<StateUpdatePayload>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.Kind, Is.EqualTo(full.Kind));
			Assert.That(actual.LocalId, Is.EqualTo(full.LocalId));
			Assert.That(actual.Reason, Is.EqualTo(full.Reason));
		});
	}

	[Test]
	public void Action_execute_result_expected_state_is_optional_and_round_trips()
	{
		var minimalJson = JsonSerializer.Serialize(new ActionExecuteResult(), PluginProtocolJson.Options);
		var oldPayload = JsonSerializer.Deserialize<ActionExecuteResult>("{\"accepted\":false}",
			PluginProtocolJson.Options);
		var json = JsonSerializer.Serialize(new ActionExecuteResult
			{
				Accepted = true, ExpectedStateId = "playing"
			},
			PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<ActionExecuteResult>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(minimalJson, Does.Not.Contain("expectedStateId"));
			Assert.That(oldPayload?.ExpectedStateId, Is.Null);
			Assert.That(actual?.Accepted, Is.True);
			Assert.That(actual?.ExpectedStateId, Is.EqualTo("playing"));
		});
	}

	[Test]
	public void Asset_begin_payload_round_trips_and_tolerates_unknown_fields()
	{
		var payload = new AssetBeginPayload
		{
			AssetId = "11111111-1111-1111-1111-111111111111",
			Kind = AssetKinds.Icon,
			MimeType = "image/png",
			TotalBytes = 4096,
			ContentHash = "sha256:abc",
		};

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<AssetBeginPayload>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.AssetId, Is.EqualTo(payload.AssetId));
			Assert.That(actual.Kind, Is.EqualTo(payload.Kind));
			Assert.That(actual.MimeType, Is.EqualTo(payload.MimeType));
			Assert.That(actual.TotalBytes, Is.EqualTo(payload.TotalBytes));
			Assert.That(actual.ContentHash, Is.EqualTo(payload.ContentHash));
		});
	}

	[Test]
	public void Asset_chunk_payload_round_trips_and_tolerates_unknown_fields()
	{
		var payload = new AssetChunkPayload
			{ AssetId = "11111111-1111-1111-1111-111111111111", Index = 3, Data = Convert.ToBase64String([1, 2, 3]) };

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<AssetChunkPayload>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.AssetId, Is.EqualTo(payload.AssetId));
			Assert.That(actual.Index, Is.EqualTo(payload.Index));
			Assert.That(actual.Data, Is.EqualTo(payload.Data));
		});
	}

	[Test]
	public void Asset_commit_payload_round_trips_and_tolerates_unknown_fields()
	{
		var payload = new AssetCommitPayload { AssetId = "11111111-1111-1111-1111-111111111111" };

		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<AssetCommitPayload>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(actual!.AssetId, Is.EqualTo(payload.AssetId));
	}

	[Test]
	public void Asset_ack_payload_index_is_optional_and_tolerates_unknown_fields()
	{
		var withoutIndex = new AssetAckPayload { AssetId = "11111111-1111-1111-1111-111111111111", Accepted = true };
		var withoutIndexJson = JsonSerializer.Serialize(withoutIndex, PluginProtocolJson.Options);
		Assert.That(withoutIndexJson, Does.Not.Contain("index"));

		var payload = new AssetAckPayload
			{ AssetId = "11111111-1111-1111-1111-111111111111", Index = 3, Accepted = false };
		var json = JsonSerializer.Serialize(payload, PluginProtocolJson.Options);
		var actual = JsonSerializer.Deserialize<AssetAckPayload>(InjectUnknownField(json), PluginProtocolJson.Options);

		Assert.That(actual, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(actual!.AssetId, Is.EqualTo(payload.AssetId));
			Assert.That(actual.Index, Is.EqualTo(payload.Index));
			Assert.That(actual.Accepted, Is.EqualTo(payload.Accepted));
		});
	}

	/// <summary>
	/// <see cref="PluginProtocolJson.Options" /> carries no <see cref="System.Text.Json.Serialization.JsonStringEnumConverter" />,
	/// so any enum-typed property on these DTOs would serialize as its underlying integer - making the
	/// enum's declaration order silently part of the wire contract. Every kind/api/operation field on
	/// these payloads must therefore stay a plain string, never an enum.
	/// </summary>
	[Test]
	public void No_dto_in_callbacks_assets_or_capabilities_exposes_an_enum_typed_property()
	{
		// Prefix-matched, not compared exactly: the per-kind DTOs live in child namespaces
		// (Capabilities.Actions, .ConfigFlow, .Ui), and an exact match would have left every one of
		// them - the payloads this rule most needs to cover - outside the guard.
		string[] roots =
		[
			typeof(HostApis).Namespace!,
			typeof(AssetKinds).Namespace!,
			typeof(StateUpdatePayload).Namespace!
		];

		var candidateTypes = typeof(HostApis).Assembly.GetTypes()
			.Where(type =>
				type.IsPublic &&
				type.Namespace is not null &&
				roots.Any(root =>
					type.Namespace == root ||
					type.Namespace.StartsWith(root + ".", StringComparison.Ordinal)));

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

	// S5 (issue #543): deserialized from literal JSON with the new key absent, never constructed in C#
	// and asserted against its default - a member marked `required` would only fail here, and a
	// tautological "new Dto().Flag == false" assertion would not catch that.

	[Test]
	public void ConfigFlowDescribePayload_deserializes_with_ServesConfigUiTree_absent_and_defaults_to_false()
	{
		var payload = JsonSerializer
			.Deserialize<ConfigFlowDescribePayload>("{\"allowsMultipleConfigurations\":true}",
				PluginProtocolJson.Options);

		Assert.That(payload, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(payload!.AllowsMultipleConfigurations, Is.True);
			Assert.That(payload.ServesConfigUiTree, Is.False);
		});

		var withUnknownFieldToo = JsonSerializer.Deserialize<
			ConfigFlowDescribePayload>("{\"allowsMultipleConfigurations\":true,\"futureField\":42}",
			PluginProtocolJson.Options);

		Assert.That(withUnknownFieldToo, Is.Not.Null);
		Assert.That(withUnknownFieldToo!.ServesConfigUiTree, Is.False);
	}

	[Test]
	public void FlowStartArguments_keep_entry_title_additive_and_optional()
	{
		const string oldPayload =
			"{\"sessionId\":\"session\",\"oAuth\":{\"redirectUri\":\"http://127.0.0.1/callback\",\"state\":\"state\"}}";
		var fromOldHost = JsonSerializer.Deserialize<FlowStartArguments>(oldPayload, PluginProtocolJson.Options);
		var fromNewHost = new FlowStartArguments
		{
			SessionId = "session",
			OAuth = new ConfigFlowOAuthContextDto
				{ RedirectUri = "http://127.0.0.1/callback", State = "state" },
			EntryTitle = "Streaming PC"
		};
		var newPayload = JsonSerializer.Serialize(fromNewHost, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(fromOldHost!.EntryTitle, Is.Null);
			Assert.That(newPayload, Does.Contain("\"entryTitle\":\"Streaming PC\""));
		});
	}

	[Test]
	public void ActionDescriptorDto_deserializes_with_ConfiguresWithUiTree_absent_and_defaults_to_false()
	{
		const string json = "{\"localId\":\"play\",\"name\":\"Play\",\"description\":\"Plays.\",\"parameters\":[]}";

		var descriptor = JsonSerializer.Deserialize<ActionDescriptorDto>(json, PluginProtocolJson.Options);

		Assert.That(descriptor, Is.Not.Null);
		Assert.That(descriptor!.ConfiguresWithUiTree, Is.False);

		var withUnknownFieldToo = JsonSerializer.Deserialize<ActionDescriptorDto>(InjectUnknownField(json),
			PluginProtocolJson.Options);

		Assert.That(withUnknownFieldToo, Is.Not.Null);
		Assert.That(withUnknownFieldToo!.ConfiguresWithUiTree, Is.False);
	}

	private static string InjectUnknownField(string json)
	{
		var body = json[..^1]; // strip only the outermost closing brace, not nested ones
		var separator = body.EndsWith('{') ? string.Empty : ",";
		return body + separator + "\"unknownField\":\"ignored\"}";
	}
}
