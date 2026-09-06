using MacroDeckHost.Integrations.System.Actions;
using MacroDeckHost.Integrations.System.Power;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.System;

public class PowerActionTests
{
	private static ActionExecutionContext Context(Dictionary<string, object>? parameters = null)
		=> new() { Parameters = parameters ?? new Dictionary<string, object>() };

	[Test]
	public async Task LockComputer_succeeds_when_service_succeeds()
	{
		var power = new FakePowerService();
		var action = new LockComputerActionDefinition(power);

		var result = await action.CreateExecutor().ExecuteAsync(Context());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(power.LastOperation, Is.EqualTo(PowerOperation.Lock));
		});
	}

	[Test]
	public async Task Sleep_fails_with_service_reason_when_service_reports_failure()
	{
		var power = new FakePowerService { Result = PowerResult.Failed("no swap configured") };
		var action = new SleepActionDefinition(power);

		var result = await action.CreateExecutor().ExecuteAsync(Context());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.EqualTo("no swap configured"));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
		});
	}

	[Test]
	public async Task Sleep_uses_the_service_supplied_error_code_when_present()
	{
		var power = new FakePowerService
		{
			Result = PowerResult.Failed("denied", ActionErrorCodes.PermissionDenied)
		};
		var action = new SleepActionDefinition(power);

		var result = await action.CreateExecutor().ExecuteAsync(Context());

		Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.PermissionDenied));
	}

	[Test]
	public async Task Hibernate_fails_as_unavailable_with_the_service_reason_when_unsupported()
	{
		var power = new FakePowerService();
		power.UnsupportedOperations.Add(PowerOperation.Hibernate);
		var action = new HibernateActionDefinition(power);

		var result = await action.CreateExecutor().ExecuteAsync(Context());

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));

			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Is.EqualTo(FakePowerService.UnsupportedReason));
		});
	}

	[Test]
	public void Hibernate_is_offered_only_on_windows_and_linux()
	{
		var action = new HibernateActionDefinition(new FakePowerService());

		Assert.That(action.Platforms, Is.EqualTo(MacroDeckPlatform.Windows | MacroDeckPlatform.Linux));
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task Restart_passes_the_force_parameter_through(bool force)
	{
		var power = new FakePowerService();
		var action = new RestartActionDefinition(power);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object> { ["force"] = force }));

		Assert.Multiple(() =>
		{
			Assert.That(power.LastOperation, Is.EqualTo(PowerOperation.Restart));
			Assert.That(power.LastForce, Is.EqualTo(force));
		});
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task ShutDown_passes_the_force_parameter_through(bool force)
	{
		var power = new FakePowerService();
		var action = new ShutDownActionDefinition(power);

		await action.CreateExecutor().ExecuteAsync(Context(new Dictionary<string, object> { ["force"] = force }));

		Assert.Multiple(() =>
		{
			Assert.That(power.LastOperation, Is.EqualTo(PowerOperation.ShutDown));
			Assert.That(power.LastForce, Is.EqualTo(force));
		});
	}

	[Test]
	public async Task Restart_defaults_force_to_false_when_not_supplied()
	{
		var power = new FakePowerService();
		var action = new RestartActionDefinition(power);

		await action.CreateExecutor().ExecuteAsync(Context());

		Assert.That(power.LastForce, Is.False);
	}
}
