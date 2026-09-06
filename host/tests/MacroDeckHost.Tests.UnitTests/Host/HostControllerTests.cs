using MacroDeckHost.Application.Lifecycle;
using System.ComponentModel;
using System.Net;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Host;
using MacroDeckHost.Application.Ui.Transport.Messages.Notifications;
using MacroDeckHost.Infrastructure.Applications;
using MacroDeckHost.Infrastructure.Lifecycle;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Integrations.System.Notifications;
using MacroDeckHost.Tests.UnitTests.System;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Host;

public class HostControllerTests
{
	private sealed class FakeLifetime : IHostApplicationLifetime
	{
		public bool StopRequested { get; private set; }

		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
			=> StopRequested = true;
	}

	private readonly List<TestPaths> _createdPaths = [];

	[TearDown]
	public void RemoveCreatedDirectories()
	{
		foreach (var paths in _createdPaths)
		{
			paths.Cleanup();
		}

		_createdPaths.Clear();
	}

	private HostController CreateController(FakeLifetime lifetime,
		int localPort,
		IPAddress? remoteAddress,
		IUserNotificationStore? userNotificationStore = null,
		string? shellExecutable = "/Applications/Macro Deck.app",
		FakeApplicationService? applicationService = null,
		TestPaths? paths = null,
		IShellNotificationBridge? shellNotifications = null)
	{
		var httpContext = new DefaultHttpContext
		{
			Connection = { LocalPort = localPort, RemoteIpAddress = remoteAddress }
		};
		httpContext.Request.Host = new HostString("127.0.0.1", localPort);

		var store = userNotificationStore ?? new UserNotificationStore();
		var restart = new ApplicationRestartService(lifetime, shellExecutable, TimeSpan.Zero);
		var apps = applicationService ?? new FakeApplicationService();
		var macroDeckPaths = paths ?? new TestPaths();
		_createdPaths.Add(macroDeckPaths);
		var reveal = new FolderRevealService(apps);
		return new HostController(Logger.None,
			lifetime,
			new ReportUpdateStateRequestMessageHandler(store,
				TestLocalization.ScopeFactory,
				TestLocalization.Resolver),
			new RestartApplicationRequestMessageHandler(restart),
			new GetDataDirectoryRequestMessageHandler(macroDeckPaths),
			new OpenDataDirectoryRequestMessageHandler(macroDeckPaths, reveal),
			new GetHostSessionRequestMessageHandler(new HostSession()),
			shellNotifications ?? new ShellNotificationBridge(TimeProvider.System))
		{
			ControllerContext = new ControllerContext { HttpContext = httpContext }
		};
	}

	[Test]
	public void Shutdown_on_public_port_returns_not_found()
	{
		var lifetime = new FakeLifetime();
		var controller = CreateController(lifetime, HostEndpoints.PublicPort, IPAddress.Loopback);

		var result = controller.Shutdown("update");

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<NotFoundResult>());
			Assert.That(lifetime.StopRequested, Is.False);
		});
	}

	[Test]
	public void Shutdown_from_non_loopback_address_returns_not_found()
	{
		var lifetime = new FakeLifetime();
		var controller = CreateController(lifetime, TestListenerPorts.Loopback, IPAddress.Parse("192.168.1.10"));

		var result = controller.Shutdown("update");

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<NotFoundResult>());
			Assert.That(lifetime.StopRequested, Is.False);
		});
	}

	[Test]
	public void Shutdown_on_loopback_with_reason_stops_the_host()
	{
		var lifetime = new FakeLifetime();
		var controller = CreateController(lifetime, TestListenerPorts.Loopback, IPAddress.Loopback);

		var result = controller.Shutdown("update");

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<OkResult>());
			Assert.That(lifetime.StopRequested, Is.True);
		});
	}

	[Test]
	public void Shutdown_on_loopback_without_reason_stops_the_host()
	{
		var lifetime = new FakeLifetime();
		var controller = CreateController(lifetime, TestListenerPorts.Loopback, IPAddress.Loopback);

		var result = controller.Shutdown();

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<OkResult>());
			Assert.That(lifetime.StopRequested, Is.True);
		});
	}

	[Test]
	public async Task Restart_on_the_public_port_still_requests_a_restart()
	{
		var lifetime = new FakeLifetime();
		var controller = CreateController(lifetime, HostEndpoints.PublicPort, IPAddress.Loopback);

		var result = await controller.Restart(new RestartApplicationRequest(), CancellationToken.None);

		var response = (result as OkObjectResult)?.Value as RestartApplicationResponse;
		Assert.That(response?.Success, Is.True);
	}

	[Test]
	public async Task Restart_from_a_remote_address_still_requests_a_restart()
	{
		var lifetime = new FakeLifetime();
		var controller = CreateController(lifetime, TestListenerPorts.Loopback, IPAddress.Parse("192.168.1.10"));

		var result = await controller.Restart(new RestartApplicationRequest(), CancellationToken.None);

		var response = (result as OkObjectResult)?.Value as RestartApplicationResponse;
		Assert.That(response?.Success, Is.True);
	}

	[Test]
	public async Task Restart_on_trusted_loopback_requests_a_restart()
	{
		var lifetime = new FakeLifetime();
		var controller = CreateController(lifetime, TestListenerPorts.Loopback, IPAddress.Loopback);

		var result = await controller.Restart(new RestartApplicationRequest { Reason = "network-port" },
			CancellationToken.None);

		var response = (result as OkObjectResult)?.Value as RestartApplicationResponse;
		Assert.Multiple(() =>
		{
			Assert.That(response, Is.Not.Null);
			Assert.That(response!.Success, Is.True);
			Assert.That(response.Supported, Is.True);
			Assert.That(response.Error, Is.Null);
		});
	}

	[Test]
	public async Task Restart_without_a_desktop_shell_reports_it_is_unsupported()
	{
		var lifetime = new FakeLifetime();
		var controller
			= CreateController(lifetime, TestListenerPorts.Loopback, IPAddress.Loopback, shellExecutable: null);

		var result = await controller.Restart(new RestartApplicationRequest(), CancellationToken.None);

		var response = (result as OkObjectResult)?.Value as RestartApplicationResponse;
		Assert.Multiple(() =>
		{
			Assert.That(response, Is.Not.Null);
			Assert.That(response!.Success, Is.False);
			Assert.That(response.Supported, Is.False);
			Assert.That(response.Error, Is.Not.Empty);
			Assert.That(lifetime.StopRequested, Is.False);
		});
	}

	[Test]
	public async Task UpdateState_on_public_port_returns_not_found()
	{
		var store = new UserNotificationStore();
		var controller = CreateController(new FakeLifetime(),
			HostEndpoints.PublicPort,
			IPAddress.Loopback,
			store);

		var result = await controller.UpdateState(
			new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<NotFoundResult>());
			Assert.That(store.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task UpdateState_from_non_loopback_address_returns_not_found()
	{
		var store = new UserNotificationStore();
		var controller = CreateController(new FakeLifetime(),
			TestListenerPorts.Loopback,
			IPAddress.Parse("192.168.1.10"),
			store);

		var result = await controller.UpdateState(
			new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<NotFoundResult>());
			Assert.That(store.Snapshot(), Is.Empty);
		});
	}

	[Test]
	public async Task UpdateState_on_trusted_loopback_raises_a_notification()
	{
		var store = new UserNotificationStore();
		var controller = CreateController(new FakeLifetime(), TestListenerPorts.Loopback, IPAddress.Loopback, store);

		var result = await controller.UpdateState(
			new ReportUpdateStateRequest { Version = "1.2.3", Phase = "available", CanInstall = true },
			CancellationToken.None);

		var notifications = store.Snapshot();
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<OkResult>());
			Assert.That(notifications, Has.Count.EqualTo(1));
			Assert.That(notifications[0].Title, Is.EqualTo("Macro Deck 1.2.3 is available"));
		});
	}

	[Test]
	public async Task OpenDataDirectory_from_a_remote_address_returns_not_found_and_opens_nothing()
	{
		var apps = new FakeApplicationService();
		var controller = CreateController(new FakeLifetime(),
			TestListenerPorts.Loopback,
			IPAddress.Parse("192.168.1.10"),
			applicationService: apps);

		var result = await controller.OpenDataDirectory(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<NotFoundResult>());
			Assert.That(apps.OpenedFolders, Is.Empty);
		});
	}

	[Test]
	public async Task OpenDataDirectory_on_the_public_port_returns_not_found_and_opens_nothing()
	{
		var apps = new FakeApplicationService();
		var controller = CreateController(new FakeLifetime(),
			HostEndpoints.PublicPort,
			IPAddress.Loopback,
			applicationService: apps);

		var result = await controller.OpenDataDirectory(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<NotFoundResult>());
			Assert.That(apps.OpenedFolders, Is.Empty);
		});
	}

	[Test]
	public async Task OpenDataDirectory_on_trusted_loopback_opens_the_instance_data_root()
	{
		var apps = new FakeApplicationService();
		var paths = new TestPaths();
		var controller = CreateController(new FakeLifetime(),
			TestListenerPorts.Loopback,
			IPAddress.Loopback,
			applicationService: apps,
			paths: paths);

		var result = await controller.OpenDataDirectory(CancellationToken.None);

		var response = (result as OkObjectResult)?.Value as OpenDataDirectoryResponse;
		Assert.Multiple(() =>
		{
			Assert.That(response, Is.Not.Null);
			Assert.That(response!.Success, Is.True);
			Assert.That(response.Error, Is.Null);
			Assert.That(apps.OpenedFolders, Has.Count.EqualTo(1));
			Assert.That(apps.OpenedFolders[0], Is.EqualTo(paths.DataRootDirectory));
			Assert.That(apps.OpenedFolders, Does.Not.Contain(paths.DataDirectory));
		});
	}

	[Test]
	public async Task OpenDataDirectory_creates_the_data_root_before_opening_it_when_it_does_not_exist_yet()
	{
		var apps = new FakeApplicationService();
		var paths = new TestPaths();
		var controller = CreateController(new FakeLifetime(),
			TestListenerPorts.Loopback,
			IPAddress.Loopback,
			applicationService: apps,
			paths: paths);

		Assert.That(Directory.Exists(paths.DataRootDirectory), Is.False);

		var result = await controller.OpenDataDirectory(CancellationToken.None);

		var response = (result as OkObjectResult)?.Value as OpenDataDirectoryResponse;
		Assert.Multiple(() =>
		{
			Assert.That(response, Is.Not.Null);
			Assert.That(response!.Success, Is.True);
			Assert.That(Directory.Exists(paths.DataRootDirectory), Is.True);
			Assert.That(apps.FolderExistedWhenOpened, Has.Count.EqualTo(1));
			Assert.That(apps.FolderExistedWhenOpened[0], Is.True);
		});
	}

	[Test]
	public async Task OpenDataDirectory_reports_a_failure_when_the_platform_command_cannot_be_spawned()
	{
		var apps = new FakeApplicationService
		{
			OpenFolderException = new Win32Exception("No such file or directory")
		};
		var controller = CreateController(new FakeLifetime(),
			TestListenerPorts.Loopback,
			IPAddress.Loopback,
			applicationService: apps);

		var result = await controller.OpenDataDirectory(CancellationToken.None);

		var response = (result as OkObjectResult)?.Value as OpenDataDirectoryResponse;
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<OkObjectResult>());
			Assert.That(response, Is.Not.Null);
			Assert.That(response!.Success, Is.False);
			Assert.That(response.Error, Is.Not.Empty);
		});
	}

	[Test]
	public async Task OpenDataDirectory_reports_a_failure_when_the_platform_has_no_application_support()
	{
		var apps = new FakeApplicationService { IsSupported = false };
		var controller = CreateController(new FakeLifetime(),
			TestListenerPorts.Loopback,
			IPAddress.Loopback,
			applicationService: apps);

		var result = await controller.OpenDataDirectory(CancellationToken.None);

		var response = (result as OkObjectResult)?.Value as OpenDataDirectoryResponse;
		Assert.Multiple(() =>
		{
			Assert.That(response, Is.Not.Null);
			Assert.That(response!.Success, Is.False);
			Assert.That(response.Error, Is.Not.Empty);
		});
	}

	[Test]
	public async Task GetDataDirectory_from_a_remote_address_returns_the_path_but_reports_it_cannot_be_opened()
	{
		var paths = new TestPaths();
		var controller = CreateController(new FakeLifetime(),
			HostEndpoints.PublicPort,
			IPAddress.Parse("192.168.1.10"),
			paths: paths);

		var result = await controller.GetDataDirectory(CancellationToken.None);

		var response = (result as OkObjectResult)?.Value as GetDataDirectoryResponse;
		Assert.Multiple(() =>
		{
			Assert.That(result, Is.InstanceOf<OkObjectResult>());
			Assert.That(response, Is.Not.Null);
			Assert.That(response!.Path, Is.EqualTo(paths.DataRootDirectory));
			Assert.That(response.CanOpen, Is.False);
		});
	}

	[Test]
	public async Task GetDataDirectory_on_trusted_loopback_reports_the_path_can_be_opened()
	{
		var paths = new TestPaths();
		var controller = CreateController(new FakeLifetime(),
			TestListenerPorts.Loopback,
			IPAddress.Loopback,
			paths: paths);

		var result = await controller.GetDataDirectory(CancellationToken.None);

		var response = (result as OkObjectResult)?.Value as GetDataDirectoryResponse;
		Assert.Multiple(() =>
		{
			Assert.That(response, Is.Not.Null);
			Assert.That(response!.Path, Is.EqualTo(paths.DataRootDirectory));
			Assert.That(response.CanOpen, Is.True);
		});
	}

	[Test]
	public async Task Shell_notifications_are_not_served_to_anything_but_the_shell()
	{
		var bridge = new ShellNotificationBridge(TimeProvider.System);
		var controller = CreateController(new FakeLifetime(),
			TestListenerPorts.Loopback,
			IPAddress.Parse("192.168.1.10"),
			shellNotifications: bridge);

		var poll = await controller.ShellNotifications(CancellationToken.None);
		var result = controller.ReportShellNotification(1, new ShellNotificationResultRequest(true));

		Assert.Multiple(() =>
		{
			Assert.That(poll, Is.InstanceOf<NotFoundResult>());
			Assert.That(result, Is.InstanceOf<NotFoundResult>());
			Assert.That(bridge.IsAttached, Is.False);
		});
	}

	[Test]
	public async Task The_shell_polls_a_queued_notification_and_reports_it_back()
	{
		var bridge = new ShellNotificationBridge(TimeProvider.System);
		var controller = CreateController(new FakeLifetime(),
			TestListenerPorts.Loopback,
			IPAddress.Loopback,
			shellNotifications: bridge);
		await Attach(bridge);

		var dispatch = bridge.TryDispatchAsync("A plugin wants to pair", "Example Plugin");
		var polled = ((await controller.ShellNotifications(CancellationToken.None)) as OkObjectResult)?.Value
			as ShellNotificationsResponse;

		Assert.That(polled, Is.Not.Null);
		Assert.That(polled!.Notifications, Has.Count.EqualTo(1));
		Assert.That(polled.Notifications[0].Title, Is.EqualTo("A plugin wants to pair"));
		Assert.That(polled.Notifications[0].Message, Is.EqualTo("Example Plugin"));

		var result = controller.ReportShellNotification(polled.Notifications[0].Id,
			new ShellNotificationResultRequest(true));

		Assert.That(result, Is.InstanceOf<OkResult>());
		Assert.That(await dispatch, Is.True);
	}

	[Test]
	public async Task A_second_poll_is_refused_while_the_shell_is_already_waiting()
	{
		var bridge = new ShellNotificationBridge(TimeProvider.System);
		var controller = CreateController(new FakeLifetime(),
			TestListenerPorts.Loopback,
			IPAddress.Loopback,
			shellNotifications: bridge);

		using var abandoned = new CancellationTokenSource();
		var poll = controller.ShellNotifications(abandoned.Token);

		var second = await controller.ShellNotifications(CancellationToken.None);

		await abandoned.CancelAsync();
		await poll;
		Assert.That(second, Is.InstanceOf<ConflictResult>());
	}

	private static async Task Attach(ShellNotificationBridge bridge)
	{
		using var abandoned = new CancellationTokenSource();
		await abandoned.CancelAsync();
		await bridge.WaitAsync(TimeSpan.FromSeconds(20), abandoned.Token);
	}
}
