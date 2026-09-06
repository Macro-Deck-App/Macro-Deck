using System.Text.Json;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Scripts;

/// <summary>
/// The wire shape the clients are written against - field names and value types, not just "it round-trips
/// through my own serializer".
/// </summary>
[TestFixture]
public class ScriptInputWireTests
{
	private static readonly JsonSerializerOptions _web = new(JsonSerializerDefaults.Web);
	private static readonly int[] _supportedProtocolVersions = [1, 2, 3];

	[Test]
	public void A_declaration_serializes_under_the_agreed_names()
	{
		var script = new Script
		{
			Id = "s1",
			Name = "Alpha",
			Inputs =
			[
				new ScriptInput
				{
					Name = "scene",
					Type = ScriptInputType.Text,
					Label = "Scene name",
					Description = "Which scene to switch to",
					Required = true,
					DefaultValue = "Starting Soon"
				}
			]
		};

		using var document = JsonDocument.Parse(JsonSerializer.Serialize(script, _web));
		var declared = document.RootElement.GetProperty("inputs").EnumerateArray().Single();

		Assert.Multiple(() =>
		{
			Assert.That(declared.GetProperty("name").GetString(), Is.EqualTo("scene"));
			Assert.That(declared.GetProperty("type").GetString(), Is.EqualTo("text"));
			Assert.That(declared.GetProperty("label").GetString(), Is.EqualTo("Scene name"));
			Assert.That(declared.GetProperty("description").GetString(), Is.EqualTo("Which scene to switch to"));
			Assert.That(declared.GetProperty("required").GetBoolean(), Is.True);
			Assert.That(declared.GetProperty("defaultValue").GetString(), Is.EqualTo("Starting Soon"));
		});
	}

	[Test]
	public void Declarations_round_trip_unchanged()
	{
		var inputs = new List<ScriptInput>
		{
			new()
			{
				Name = "scene",
				Type = ScriptInputType.Text,
				Label = "Scene name",
				Required = true,
				DefaultValue = "Starting Soon"
			},
			new() { Name = "volume", Type = ScriptInputType.Numeric },
			new() { Name = "muted", Type = ScriptInputType.Boolean }
		};

		var json = JsonSerializer.Serialize(new Script { Id = "s1", Name = "Alpha", Inputs = inputs }, _web);
		var round = JsonSerializer.Deserialize<Script>(json, _web)!;

		Assert.That(JsonSerializer.Serialize(round, _web), Is.EqualTo(json));
	}

	[Test]
	public void Absent_declarations_read_as_an_empty_list()
	{
		var script = JsonSerializer.Deserialize<Script>("""{"id":"s1","name":"Alpha"}""", _web)!;

		Assert.That(script.Inputs, Is.Not.Null.And.Empty);
	}

	// A caller hands a variable straight through to an input, so the two type sets have to agree
	// member for member and letter for letter on the wire.
	[Test]
	public void The_input_types_are_the_variable_types()
	{
		var inputs = Enum.GetValues<ScriptInputType>()
			.Select(type => new ScriptInput { Name = $"i{(int)type}", Type = type })
			.ToList();

		using var document = JsonDocument.Parse(
			JsonSerializer.Serialize(new Script { Id = "s1", Name = "Alpha", Inputs = inputs }, _web));
		var spellings = document.RootElement.GetProperty("inputs").EnumerateArray()
			.Select(declared => declared.GetProperty("type").GetString())
			.ToList();

		Assert.Multiple(() =>
		{
			Assert.That(Enum.GetValues<ScriptInputType>().Select(type => ((int)type, type.ToString())),
				Is.EqualTo(Enum.GetValues<VariableType>().Select(type => ((int)type, type.ToString()))));
			Assert.That(spellings,
				Is.EqualTo(Enum.GetNames<VariableType>().Select(name => name.ToLowerInvariant())));
		});
	}

	[Test]
	public void A_supplied_numeric_value_arrives_as_a_number()
	{
		var request = JsonSerializer.Deserialize<RunScriptRequest>(
			"""{"id":"s1","inputs":{"scene":"Live","volume":42,"muted":true}}""",
			_web)!;

		Assert.Multiple(() =>
		{
			Assert.That(((JsonElement)request.Inputs!["volume"]!).ValueKind, Is.EqualTo(JsonValueKind.Number));
			Assert.That(((JsonElement)request.Inputs["volume"]!).GetDouble(), Is.EqualTo(42d));
			Assert.That(((JsonElement)request.Inputs["scene"]!).GetString(), Is.EqualTo("Live"));
			Assert.That(((JsonElement)request.Inputs["muted"]!).ValueKind, Is.EqualTo(JsonValueKind.True));
		});
	}

	[Test]
	public void RunsOnWidget_round_trips()
	{
		var json = JsonSerializer.Serialize(new Script { Id = "s1", Name = "Alpha", RunsOnWidget = true }, _web);
		using var document = JsonDocument.Parse(json);

		Assert.That(document.RootElement.GetProperty("runsOnWidget").GetBoolean(), Is.True);
		Assert.That(JsonSerializer.Deserialize<Script>(json, _web)!.RunsOnWidget, Is.True);
	}

	// A script document written before this field existed - an older peer's document, or a peer that
	// still ignores it - must still deserialize, with the flag defaulting to false.
	[Test]
	public void A_script_written_without_RunsOnWidget_still_deserializes()
	{
		var script = JsonSerializer.Deserialize<Script>("""{"id":"s1","name":"Alpha"}""", _web)!;

		Assert.That(script.RunsOnWidget, Is.False);
	}

	[Test]
	public void A_run_request_written_without_an_owner_widget_still_deserializes()
	{
		var request = JsonSerializer.Deserialize<RunScriptRequest>("""{"id":"s1"}""", _web)!;

		Assert.That(request.OwnerWidgetId, Is.Null);
	}

	// Acceptance scenario 6: the widget-owned run feature is additive - it must not have bumped the
	// protocol major, and ScriptInputType must still be exactly its three original members, at their
	// original ordinals, with their original wire spellings.
	[Test]
	public void The_protocol_stays_at_its_pre_feature_versions()
	{
		Assert.Multiple(() =>
		{
			Assert.That(ProtocolVersions.Current, Is.EqualTo(3));
			Assert.That(ProtocolVersions.Minimum, Is.EqualTo(1));
			Assert.That(ProtocolVersions.Supported, Is.EqualTo(_supportedProtocolVersions));
		});
	}

	[Test]
	public void ScriptInputType_has_exactly_its_three_original_members()
	{
		Assert.Multiple(() =>
		{
			Assert.That((int)ScriptInputType.Text, Is.EqualTo(0));
			Assert.That((int)ScriptInputType.Numeric, Is.EqualTo(1));
			Assert.That((int)ScriptInputType.Boolean, Is.EqualTo(2));
			Assert.That(Enum.GetValues<ScriptInputType>(), Has.Length.EqualTo(3));
		});
	}

	[TestCase(ScriptInputType.Text, "text")]
	[TestCase(ScriptInputType.Numeric, "numeric")]
	[TestCase(ScriptInputType.Boolean, "boolean")]
	public void ScriptInputType_writes_its_original_lowercase_wire_spelling(ScriptInputType type, string wireValue)
	{
		var script = new Script { Id = "s1", Name = "Alpha", Inputs = [new ScriptInput { Name = "i", Type = type }] };

		using var document = JsonDocument.Parse(JsonSerializer.Serialize(script, _web));

		Assert.That(document.RootElement.GetProperty("inputs")[0].GetProperty("type").GetString(),
			Is.EqualTo(wireValue));
	}

	[Test]
	public void A_run_response_reports_the_applied_input_names()
	{
		var json = JsonSerializer.Serialize(new RunScriptResponse { Success = true, AppliedInputs = ["scene"] }, _web);

		using var document = JsonDocument.Parse(json);

		Assert.That(document.RootElement.GetProperty("appliedInputs").EnumerateArray().Single().GetString(),
			Is.EqualTo("scene"));
	}

	// The narrowed meaning of "applied" (caller-supplied-and-accepted, not merely "ended up with a
	// value") must not change the wire shape: still a JSON array of strings, still present as [] rather
	// than omitted when a run applied nothing.
	[Test]
	public void A_run_response_with_no_applied_inputs_still_reports_an_empty_array()
	{
		var json = JsonSerializer.Serialize(new RunScriptResponse { Success = true }, _web);

		using var document = JsonDocument.Parse(json);
		var appliedInputs = document.RootElement.GetProperty("appliedInputs");

		Assert.Multiple(() =>
		{
			Assert.That(appliedInputs.ValueKind, Is.EqualTo(JsonValueKind.Array));
			Assert.That(appliedInputs.GetArrayLength(), Is.EqualTo(0));
		});
	}
}
