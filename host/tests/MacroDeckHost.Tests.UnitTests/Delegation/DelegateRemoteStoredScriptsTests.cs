using MacroDeckHost.Integrations.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Delegation;

/// <summary>
/// The remote script list is cached in the integration's config, so the reader has to keep understanding
/// what earlier versions of Macro Deck wrote there.
/// </summary>
[TestFixture]
public class DelegateRemoteStoredScriptsTests
{
	[Test]
	public void An_entry_written_before_inputs_existed_still_reads_as_a_script()
	{
		var parsed = DelegateRemote.ParseScripts("""{"s1":"Alpha","s2":"Beta"}""");

		Assert.Multiple(() =>
		{
			Assert.That(parsed["s1"].Id, Is.EqualTo("s1"));
			Assert.That(parsed["s1"].Name, Is.EqualTo("Alpha"));
			Assert.That(parsed["s1"].Inputs, Is.Empty);
			Assert.That(parsed["s2"].Name, Is.EqualTo("Beta"));
		});
	}

	[Test]
	public void An_entry_carrying_declarations_keeps_them()
	{
		const string stored = """
							  {
							    "s1": {
							      "id": "s1",
							      "name": "Alpha",
							      "inputs": [
							        { "name": "volume", "type": "numeric", "label": "Volume", "required": true,
							          "defaultValue": "3" }
							      ]
							    }
							  }
							  """;

		var declared = DelegateRemote.ParseScripts(stored)["s1"].Inputs.Single();

		Assert.Multiple(() =>
		{
			Assert.That(declared.Name, Is.EqualTo("volume"));
			Assert.That(declared.Type, Is.EqualTo(MacroDeck.Sdk.Scripts.ScriptInputType.Numeric));
			Assert.That(declared.Label, Is.EqualTo("Volume"));
			Assert.That(declared.Required, Is.True);
			Assert.That(declared.DefaultValue, Is.EqualTo("3"));
		});
	}

	[Test]
	public void An_entry_with_no_declarations_reads_as_an_empty_list_not_null()
	{
		var parsed = DelegateRemote.ParseScripts("""{"s1":{"id":"s1","name":"Alpha"}}""");

		Assert.That(parsed["s1"].Inputs, Is.Empty);
	}

	[TestCase(null)]
	[TestCase("")]
	[TestCase("not json")]
	[TestCase("[]")]
	public void Nothing_usable_reads_as_no_scripts(string? stored)
		=> Assert.That(DelegateRemote.ParseScripts(stored), Is.Empty);
}
