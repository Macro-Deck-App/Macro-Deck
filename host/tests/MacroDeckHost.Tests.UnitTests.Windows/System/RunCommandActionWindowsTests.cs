using MacroDeckHost.Integrations.System.Actions;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Tests.UnitTests.Windows.System;

[Platform("Win")]
public class RunCommandActionWindowsTests
{
	[Test]
	public async Task RunCommand_cmd_exe_preserves_inner_quotes()
	{
		var userVariables = new RecordingUserVariableApi();
		var action = new RunCommandActionDefinition(new VariableApiAccessor { UserVariables = userVariables });

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

		Assert.That(userVariables.Values.TryGetValue("result", out var value), Is.True);
		Assert.That(value?.Trim(), Is.EqualTo("\"hello quoted\""));
	}

	private sealed class RecordingUserVariableApi : IUserVariableApi
	{
		public Dictionary<string, string?> Values { get; } = new();

		public Task<UserVariableWriteResult> ApplyAsync(
			string name,
			string? ownerWidgetId,
			UserVariableOperation operation,
			string? value,
			CancellationToken cancellationToken = default)
		{
			if (!Values.ContainsKey(name))
			{
				return Task.FromResult(UserVariableWriteResult.Failed(UserVariableWriteStatus.NotFound, name));
			}

			Values[name] = value;
			return Task.FromResult(UserVariableWriteResult.Applied());
		}

		public Task<UserVariableCreateResult> CreateAsync(
			string name,
			string? ownerWidgetId,
			VariableType type,
			string? initialValue = null,
			int? decimalPlaces = null,
			CancellationToken cancellationToken = default)
		{
			Values[name] = initialValue;
			return Task.FromResult(UserVariableCreateResult.Created());
		}
	}
}
