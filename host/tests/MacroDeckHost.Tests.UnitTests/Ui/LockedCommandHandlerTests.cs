using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Scripts;
using MacroDeckHost.Application.Ui.Transport.Messages.Variables;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Ui;

/// <summary>
/// A locked host must refuse the client commands that reach action execution indirectly, not only the
/// ones that call an executor. A variable write fires variable.changed triggers and onStateChange
/// flows, and a script run reaches the flow executor - either would otherwise let any client run
/// actions on a locked machine through a different API path.
/// </summary>
public class LockedCommandHandlerTests
{
	// The collaborators are deliberately null: the refusal has to happen before the handler touches
	// the write path at all, so a guard placed after the lookup would fail here with a
	// NullReferenceException instead of quietly executing.
	[Test]
	public async Task Setting_a_variable_is_refused_while_locked_before_the_write_path_is_entered()
	{
		var handler = new SetVariableValueRequestMessageHandler(null!, new FakeHostLockState { IsLocked = true });

		var response = await handler.Handle(new SetVariableValueRequest { Id = Guid.NewGuid().ToString(), Value = "1" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo(ActionExecutionErrorCodes.HostLocked));
		});
	}

	[Test]
	public async Task Running_a_script_is_refused_while_locked_before_the_script_is_looked_up()
	{
		var handler = new RunScriptRequestMessageHandler(null!, null!, new FakeHostLockState { IsLocked = true });

		var response = await handler.Handle(new RunScriptRequest { Id = Guid.NewGuid().ToString() },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error?.Code, Is.EqualTo(ActionExecutionErrorCodes.HostLocked));
		});
	}
}
