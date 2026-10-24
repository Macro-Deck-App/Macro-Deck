using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.Issues;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

[TestFixture]
public class IntegrationIssueHelpUrlWireTests
{
	[Test]
	public void An_issue_from_an_older_plugin_has_no_help_link_and_a_new_one_round_trips()
	{
		var old = JsonSerializer.Deserialize<IntegrationIssueDescriptorDto>(
			"""{"id":"a","title":"A","severity":"Warning"}""",
			PluginProtocolJson.Options)!;
		var json = JsonSerializer.Serialize(new IntegrationIssueDescriptorDto
			{
				Id = "a",
				Title = "A",
				Severity = "Warning",
				HelpUrl = "https://example.com/help"
			},
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(old.HelpUrl, Is.Null);
			Assert.That(JsonSerializer.Deserialize<IntegrationIssueDescriptorDto>(json, PluginProtocolJson.Options)!
					.HelpUrl,
				Is.EqualTo("https://example.com/help"));
		});
	}
}
