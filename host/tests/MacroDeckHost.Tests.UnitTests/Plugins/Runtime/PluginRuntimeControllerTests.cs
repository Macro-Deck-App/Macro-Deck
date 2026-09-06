using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Plugins.Runtime;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Runtime;

[TestFixture]
internal sealed class PluginRuntimeControllerTests
{
	[Test]
	public void GetAll_projects_snapshots_to_lower_case_wire_strings()
	{
		var supervisor = new FakePluginSupervisor();
		supervisor.SnapshotToReturn.Add(new PluginRuntimeSnapshot
		{
			PluginId = "com.example.plugin",
			DisplayName = "Example",
			Version = "1.0.0",
			State = PluginRuntimeState.Running,
			Health = PluginHealthState.Degraded,
			Managed = true,
			LastStopReason = PluginStopReason.Crash
		});

		var controller = new PluginRuntimeController(supervisor);

		var response = controller.GetAll();

		Assert.That(response.Plugins, Has.Count.EqualTo(1));
		var body = response.Plugins[0];
		Assert.Multiple(() =>
		{
			Assert.That(body.State, Is.EqualTo("running"));
			Assert.That(body.Health, Is.EqualTo("degraded"));
			Assert.That(body.LastStopReason, Is.EqualTo("crash"));
			Assert.That(body.Managed, Is.True);
		});
	}

	[TestCase(PluginSupervisorError.NotInstalled, "not_installed")]
	[TestCase(PluginSupervisorError.ManifestInvalid, "manifest_invalid")]
	[TestCase(PluginSupervisorError.NoEntrypointForRuntime, "no_entrypoint")]
	[TestCase(PluginSupervisorError.AlreadyRunning, "already_running")]
	[TestCase(PluginSupervisorError.NotRunning, "not_running")]
	[TestCase(PluginSupervisorError.SelfRegistering, "self_registering")]
	[TestCase(PluginSupervisorError.LaunchFailed, "launch_failed")]
	public async Task Start_maps_every_supervisor_error_to_the_matching_transport_error_code(
		PluginSupervisorError error,
		string expectedCode)
	{
		var supervisor = new FakePluginSupervisor();
		supervisor.StartResults["com.example.plugin"] = PluginSupervisorResult.Fail(error, "detail");
		var controller = new PluginRuntimeController(supervisor);

		var response = await controller.Start("com.example.plugin", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.False);
			Assert.That(response.Error!.Code, Is.EqualTo(expectedCode));
		});
	}

	[Test]
	public async Task Stop_always_requests_UserRequested_never_an_internal_reason()
	{
		var supervisor = new FakePluginSupervisor();
		var controller = new PluginRuntimeController(supervisor);

		await controller.Stop("com.example.plugin", CancellationToken.None);

		Assert.That(supervisor.StopCalls, Has.Count.EqualTo(1));
		Assert.That(supervisor.StopCalls[0].Reason, Is.EqualTo(PluginStopReason.UserRequested));
	}

	[Test]
	public async Task Restart_delegates_to_the_supervisor_and_reports_success()
	{
		var supervisor = new FakePluginSupervisor();
		var controller = new PluginRuntimeController(supervisor);

		var response = await controller.Restart("com.example.plugin", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Success, Is.True);
			Assert.That(supervisor.RestartCalls, Has.Count.EqualTo(1));
			Assert.That(supervisor.RestartCalls[0], Is.EqualTo("com.example.plugin"));
		});
	}
}
