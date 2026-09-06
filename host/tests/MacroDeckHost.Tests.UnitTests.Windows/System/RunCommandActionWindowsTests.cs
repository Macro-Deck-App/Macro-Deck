using MacroDeckHost.Integrations.System.Actions;
using MacroDeckHost.Tests.UnitTests.System;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Windows.System;

[Platform("Win")]
public class RunCommandActionWindowsTests
{
	[Test]
	public async Task RunCommand_cmd_exe_preserves_inner_quotes()
	{
		var api = new RecordingVariableApi();
		var action = new RunCommandActionDefinition(new VariableApiAccessor { Current = api });

		await action.CreateExecutor().ExecuteAsync(new ActionExecutionContext
		{
			Parameters = new Dictionary<string, object>
			{
				["shell"] = "default",
				["command"] = "\"cmd.exe\" /c echo \"hello quoted\"",
				["outputVariable"] = "result",
				["timeout"] = 30
			}
		});

		var handle = await api.GetByNameAsync("result");
		Assert.That(handle, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(api.CreateCount, Is.EqualTo(1));
			Assert.That(handle!.Value?.ToString()?.Trim(), Is.EqualTo("\"hello quoted\""));
		});
	}
}
