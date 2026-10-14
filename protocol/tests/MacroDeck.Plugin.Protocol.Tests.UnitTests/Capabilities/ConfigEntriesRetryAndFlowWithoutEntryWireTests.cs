using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigEntries;
using MacroDeck.Plugin.Protocol.Capabilities.ConfigFlow;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

[TestFixture]
public class ConfigEntriesRetryAndFlowWithoutEntryWireTests
{
	[Test]
	public void A_prepare_from_a_host_without_retries_reads_as_a_first_attempt()
	{
		var arguments = JsonSerializer.Deserialize<ConfigEntryPrepareArguments>(
			"{\"entryId\":\"" + Guid.NewGuid() + "\",\"title\":\"Home\",\"values\":{}}",
			PluginProtocolJson.Options)!;

		Assert.That(arguments.RejectedVariableNames, Is.Null);
	}

	[Test]
	public void A_prepare_result_from_a_plugin_without_retries_does_not_ask_for_one()
	{
		var result = JsonSerializer.Deserialize<ConfigEntryPrepareResult>("""{"success":true,"reservedVariables":[]}""",
			PluginProtocolJson.Options)!;

		Assert.That(result.RetryOnConflict, Is.Null);
	}

	[Test]
	public void A_host_without_the_flag_reads_a_completion_without_entry_as_a_plain_completion_without_title()
	{
		var json = JsonSerializer.Serialize(new ConfigFlowResultDto { Kind = "Complete", WithoutEntry = true },
			PluginProtocolJson.Options);

		var previous = JsonSerializer.Deserialize<PreviousConfigFlowResultDto>(json, PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(previous.Kind, Is.EqualTo("Complete"));
			Assert.That(previous.EntryTitle, Is.Null);
			Assert.That(previous.Values, Is.Null);
		});
	}

	[Test]
	public void The_new_fields_stay_off_the_wire_unless_they_are_used()
	{
		var arguments = JsonSerializer.Serialize(new ConfigEntryPrepareArguments
			{
				EntryId = Guid.NewGuid(), Title = "Home", Values = new Dictionary<string, JsonElement>()
			},
			PluginProtocolJson.Options);
		var result = JsonSerializer.Serialize(new ConfigEntryPrepareResult { Success = true },
			PluginProtocolJson.Options);
		var flow = JsonSerializer.Serialize(new ConfigFlowResultDto { Kind = "Complete" }, PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(arguments, Does.Not.Contain("rejectedVariableNames"));
			Assert.That(result, Does.Not.Contain("retryOnConflict"));
			Assert.That(flow, Does.Not.Contain("withoutEntry"));
		});
	}

	private sealed record PreviousConfigFlowResultDto
	{
		public required string Kind { get; init; }

		public string? EntryTitle { get; init; }

		public IReadOnlyDictionary<string, ConfigFlowValueDto>? Values { get; init; }
	}
}
