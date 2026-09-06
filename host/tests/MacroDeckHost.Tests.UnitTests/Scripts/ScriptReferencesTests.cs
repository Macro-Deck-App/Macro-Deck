using MacroDeckHost.Application.Scripts;

namespace MacroDeckHost.Tests.UnitTests.Scripts;

[TestFixture]
public class ScriptReferencesTests
{
	[Test]
	public void Extract_FindsAScriptIdInsideAnActionParameter()
	{
		var scriptId = Guid.NewGuid();
		var data = $$"""
					 { "flows": "[{\"children\":[{\"actionId\":\"run-script\",\"parameters\":[
					 {\"name\":\"scriptId\",\"value\":\"{{scriptId}}\"}]}]}]" }
					 """;

		Assert.That(ScriptReferences.Extract(data, new HashSet<Guid> { scriptId }),
			Is.EquivalentTo(new[] { scriptId }));
	}

	[Test]
	public void Extract_IgnoresGuidsThatAreNotScripts()
	{
		var iconId = Guid.NewGuid();

		Assert.That(ScriptReferences.Extract($"{{\"iconId\":\"{iconId}\"}}", new HashSet<Guid> { Guid.NewGuid() }),
			Is.Empty);
	}

	[Test]
	public void Extract_ReturnsNothingWhenNoScriptsExist()
	{
		Assert.That(ScriptReferences.Extract($"{{\"id\":\"{Guid.NewGuid()}\"}}", new HashSet<Guid>()), Is.Empty);
	}

	[Test]
	public void ExpandTransitively_FollowsScriptsThatRunOtherScripts()
	{
		var first = Guid.NewGuid();
		var second = Guid.NewGuid();
		var third = Guid.NewGuid();
		var flows = new Dictionary<Guid, string?>
		{
			[first] = $"{{\"scriptId\":\"{second}\"}}",
			[second] = $"{{\"scriptId\":\"{third}\"}}",
			[third] = "{}"
		};

		var resolved = ScriptReferences.ExpandTransitively([first],
			new HashSet<Guid> { first, second, third },
			id => flows[id]);

		Assert.That(resolved, Is.EquivalentTo(new[] { first, second, third }));
	}

	[Test]
	public void ExpandTransitively_TerminatesOnACycle()
	{
		var first = Guid.NewGuid();
		var second = Guid.NewGuid();
		var flows = new Dictionary<Guid, string?>
		{
			[first] = $"{{\"scriptId\":\"{second}\"}}",
			[second] = $"{{\"scriptId\":\"{first}\"}}"
		};

		var resolved
			= ScriptReferences.ExpandTransitively([first], new HashSet<Guid> { first, second }, id => flows[id]);

		Assert.That(resolved, Is.EquivalentTo(new[] { first, second }));
	}
}
