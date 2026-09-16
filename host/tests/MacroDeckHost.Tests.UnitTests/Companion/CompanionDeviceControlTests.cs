using System.Reflection;
using System.Security.Claims;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using MacroDeckHost.Api.Controllers;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Companion.Actions;
using MacroDeckHost.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using ActionResult = MacroDeck.Sdk.Actions.ActionResult;
using VariableClassification = MacroDeckHost.Domain.Enums.VariableClassification;
using VariableScope = MacroDeckHost.Domain.Enums.VariableScope;

namespace MacroDeckHost.Tests.UnitTests.Companion;

[TestFixture]
internal sealed class CompanionDeviceControlTests
{
	private const string PathVariable = "screenshot_path";

	private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

	private string _folder = null!;

	[SetUp]
	public void CreateFolderName()
		=> _folder = Path.Combine(Path.GetTempPath(), $"companion-screenshots-{Guid.NewGuid():N}");

	[TearDown]
	public void DeleteFolder()
	{
		if (Directory.Exists(_folder))
		{
			Directory.Delete(_folder, recursive: true);
		}
	}

	[TestCase("screen-on", "screenOn", "screenOn")]
	[TestCase("screen-off", "screenOff", "screenOff")]
	[TestCase("focus-host", "focus", "focus")]
	public async Task A_device_control_is_sent_only_when_the_device_reports_its_capability(string actionId,
		string capability,
		string command)
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device);

		var refused = await CompanionStateAndActionsTests.ExecuteAsync(harness, actionId, device);
		var sentWhileRefused = harness.Transport.GroupMessages.Count;
		await harness.ReportAsync("connection-1", device, CompanionHarness.Report(capabilities: capability));
		var accepted = await CompanionStateAndActionsTests.ExecuteAsync(harness, actionId, device);

		Assert.Multiple(() =>
		{
			Assert.That(refused.ErrorCode, Is.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(sentWhileRefused, Is.Zero);
			Assert.That(accepted.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(((CompanionCommandEvent)harness.Transport.GroupMessages.Single().Message).Command,
				Is.EqualTo(command));
		});
	}

	[Test]
	public async Task Every_missing_capability_is_refused_with_its_own_reason()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device);

		var results = new[]
		{
			await CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-on", device),
			await CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-off", device),
			await CompanionStateAndActionsTests.ExecuteAsync(harness, "focus-host", device),
			await Screenshot(harness, device, TakeScreenshotAction.DeckMode),
			await Screenshot(harness, device, TakeScreenshotAction.FullMode)
		};

		Assert.Multiple(() =>
		{
			Assert.That(results.Select(result => result.ErrorCode), Is.All.EqualTo(ActionErrorCodes.Unavailable));
			Assert.That(results.Select(result => result.ErrorMessage).Distinct().Count(), Is.EqualTo(results.Length));
			Assert.That(results[3].ErrorMessage,
				Is.EqualTo((LocalizedText)AppStrings.Integrations.Companion.Errors.DeckNotOnScreen()));
			Assert.That(harness.Transport.GroupMessages, Is.Empty);
		});
	}

	[Test]
	public async Task An_uploaded_screenshot_is_written_to_the_folder_and_its_path_to_the_variable()
	{
		var harness = new CompanionHarness();
		var device = await ConnectAsync(harness, "screenshotFull");

		var running = Screenshot(harness, device, TakeScreenshotAction.FullMode, PathVariable);
		var command = SentCommand(harness);
		var claim = Controller(harness, device).ClaimScreenshot(command.RequestId!);
		var upload = await Controller(harness, device, _png).Upload(command.RequestId!, CancellationToken.None);
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		var file = Directory.GetFiles(_folder).Single();
		var variable = await harness.Variables.Resolve(PathVariable, VariableScope.Global, null);
		Assert.Multiple(() =>
		{
			Assert.That(command.Command, Is.EqualTo("screenshot"));
			Assert.That(command.ScreenshotMode, Is.EqualTo("full"));
			Assert.That(claim, Is.InstanceOf<NotFoundResult>(), "an old device keeps the flow without a claim");
			Assert.That(upload, Is.InstanceOf<NoContentResult>());
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(Path.GetFileName(file), Does.Match(@"^companion-screenshot-\d{8}-\d{6}-\d{3}\.png$"));
			Assert.That(File.ReadAllBytes(file), Is.EqualTo(_png));
			Assert.That(variable?.Classification, Is.EqualTo(VariableClassification.User));
			Assert.That(VariableValueSerializer.Deserialize(variable!.Type, variable.Value), Is.EqualTo(file));
		});
	}

	[Test]
	public async Task An_upload_from_another_device_is_not_found_and_completes_nothing()
	{
		var harness = new CompanionHarness();
		var device = await ConnectAsync(harness, "screenshotDeck");
		var other = harness.AddDevice("Tablet");
		await harness.ReportAsync("connection-2", other);

		var running = Screenshot(harness, device, TakeScreenshotAction.DeckMode);
		var requestId = SentCommand(harness).RequestId!;
		var foreign = await Controller(harness, other, _png).Upload(requestId, CancellationToken.None);
		var completedByForeignUpload = running.IsCompleted;
		var own = await Controller(harness, device, _png).Upload(requestId, CancellationToken.None);
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(foreign, Is.InstanceOf<NotFoundResult>());
			Assert.That(completedByForeignUpload, Is.False);
			Assert.That(own, Is.InstanceOf<NoContentResult>());
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		});
	}

	[Test]
	public async Task An_unknown_or_late_upload_is_not_found_and_writes_nothing()
	{
		var harness = new CompanionHarness();
		var device = await ConnectAsync(harness, "screenshotDeck");

		var running = Screenshot(harness, device, TakeScreenshotAction.DeckMode);
		var requestId = SentCommand(harness).RequestId!;
		var unknown = await Controller(harness, device, _png).Upload("unknown", CancellationToken.None);
		harness.Time.Advance(CompanionCommandRequests.Deadline + TimeSpan.FromSeconds(1));
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
		var late = await Controller(harness, device, _png).Upload(requestId, CancellationToken.None);
		var lateFailure = Controller(harness, device)
			.ScreenshotFailed(requestId, new CompanionScreenshotFailedRequest { Reason = "failed" });

		Assert.Multiple(() =>
		{
			Assert.That(unknown, Is.InstanceOf<NotFoundResult>());
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Timeout));
			Assert.That(result.ErrorMessage,
				Is.EqualTo((LocalizedText)AppStrings.Integrations.Companion.Errors.ScreenshotTimedOut()));
			Assert.That(late, Is.InstanceOf<NotFoundResult>());
			Assert.That(lateFailure, Is.InstanceOf<NotFoundResult>());
			Assert.That(Directory.Exists(_folder), Is.False);
		});
	}

	[Test]
	public async Task An_upload_over_16_MB_is_refused_as_too_large()
	{
		var harness = new CompanionHarness();
		var device = await ConnectAsync(harness, "screenshotDeck");
		var oversized = new byte[CompanionScreenshotsController.MaxUploadBytes + 1];
		_png.CopyTo(oversized, 0);

		var running = Screenshot(harness, device, TakeScreenshotAction.DeckMode);
		var requestId = SentCommand(harness).RequestId!;
		var upload = await Controller(harness, device, oversized).Upload(requestId, CancellationToken.None);
		var limit = typeof(CompanionScreenshotsController)
			.GetMethod(nameof(CompanionScreenshotsController.Upload))!
			.GetCustomAttribute<RequestSizeLimitAttribute>() as IRequestSizeLimitMetadata;

		Assert.Multiple(() =>
		{
			Assert.That((upload as StatusCodeResult)?.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
			Assert.That(limit?.MaxRequestBodySize, Is.EqualTo(16 * 1024 * 1024));
			Assert.That(running.IsCompleted, Is.False);
			Assert.That(Directory.Exists(_folder), Is.False);
		});
	}

	[Test]
	public async Task A_body_that_is_not_a_png_is_rejected_and_fails_the_request()
	{
		var harness = new CompanionHarness();
		var device = await ConnectAsync(harness, "screenshotDeck");

		var running = Screenshot(harness, device, TakeScreenshotAction.DeckMode);
		var upload = await Controller(harness, device, "not a png"u8.ToArray())
			.Upload(SentCommand(harness).RequestId!, CancellationToken.None);
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(upload, Is.InstanceOf<BadRequestResult>());
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
			Assert.That(Directory.Exists(_folder), Is.False);
		});
	}

	[TestCase("consentDenied", ActionErrorCodes.PermissionDenied)]
	[TestCase("unavailable", ActionErrorCodes.Unavailable)]
	[TestCase("failed", ActionErrorCodes.ProviderError)]
	public async Task A_refusal_from_the_device_fails_the_action_with_its_reason(string reason, string errorCode)
	{
		var harness = new CompanionHarness();
		var device = await ConnectAsync(harness, "screenshotDeck");

		var running = Screenshot(harness, device, TakeScreenshotAction.DeckMode);
		var reply = Controller(harness, device)
			.ScreenshotFailed(SentCommand(harness).RequestId!,
				new CompanionScreenshotFailedRequest { Reason = reason });
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(reply, Is.InstanceOf<NoContentResult>());
			Assert.That(result.ErrorCode, Is.EqualTo(errorCode));
			Assert.That(result.ErrorMessage, Is.Not.EqualTo(default(LocalizedText)));
		});
	}

	[Test]
	public async Task A_request_survives_one_of_two_connections_closing()
	{
		var harness = new CompanionHarness();
		var device = await ConnectAsync(harness, "screenshotDeck");
		await harness.ReportAsync("connection-2", device, CompanionHarness.Report(capabilities: "screenshotDeck"));

		var running = Screenshot(harness, device, TakeScreenshotAction.DeckMode);
		harness.DeviceRegistry.Disconnected("connection-1");
		var upload = await Controller(harness, device, _png)
			.Upload(SentCommand(harness).RequestId!, CancellationToken.None);
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(upload, Is.InstanceOf<NoContentResult>());
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		});
	}

	[Test]
	public async Task Closing_the_last_connection_fails_the_request_as_not_connected()
	{
		var harness = new CompanionHarness();
		var device = await ConnectAsync(harness, "screenshotDeck");

		var running = Screenshot(harness, device, TakeScreenshotAction.DeckMode);
		harness.DeviceRegistry.Disconnected("connection-1");
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
		var upload = await Controller(harness, device, _png)
			.Upload(SentCommand(harness).RequestId!, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(upload, Is.InstanceOf<NotFoundResult>());
		});
	}

	[Test]
	public async Task Removing_the_device_fails_the_request()
	{
		var harness = new CompanionHarness();
		var device = await ConnectAsync(harness, "screenshotDeck");

		var running = Screenshot(harness, device, TakeScreenshotAction.DeckMode);
		await harness.DeviceRegistry.RemoveDeviceAsync(device, CancellationToken.None);
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotFound));
			Assert.That(result.ErrorMessage,
				Is.EqualTo((LocalizedText)AppStrings.Integrations.Companion.Errors.ConfigurationNotFound()));
		});
	}

	[Test]
	public async Task A_report_keeps_only_known_capabilities_once_and_clamps_the_new_values()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		using var dispatcher = CompanionStateAndActionsTests.Dispatcher(harness,
			new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthDefaults.DeviceClaim, device.ToString())], "test")));

		await dispatcher.DispatchAsync("ReportCompanionState",
			CompanionStateAndActionsTests.Payload(new
			{
				capabilities = new[] { "screenOn", "screenOn", "rootShell", "focus", "memory" },
				inFocus = true,
				networkType = "satellite",
				networkMetered = true,
				networkValidated = false,
				networkName = new string('n', 100),
				cpuUsagePercent = 250,
				memoryUsedPercent = -4
			}),
			CancellationToken.None);
		await harness.DeviceRegistry.CreationFor(device).WaitAsync(TimeSpan.FromSeconds(5));

		harness.DeviceRegistry.TryGetState(device, out var state);
		Assert.Multiple(() =>
		{
			Assert.That(state.Capabilities, Is.EquivalentTo(new[] { "screenOn", "focus", "memory" }));
			Assert.That(state.InFocus, Is.True);
			Assert.That(state.NetworkType, Is.Null);
			Assert.That(state.NetworkMetered, Is.True);
			Assert.That(state.NetworkValidated, Is.False);
			Assert.That(state.NetworkName, Has.Length.EqualTo(64));
			Assert.That(state.CpuUsagePercent, Is.EqualTo(100));
			Assert.That(state.MemoryUsedPercent, Is.Zero);
		});
	}

	[Test]
	public async Task The_new_variables_read_the_reported_values_and_go_unavailable_when_missing_or_disconnected()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		var slots = new[]
		{
			"in_focus", "network_type", "network_metered", "network_validated", "network_name", "cpu_usage_percent",
			"memory_used_percent"
		};
		await harness.ReportAsync("connection-1", device);
		var notReported = new List<VariableReading>();
		foreach (var slot in slots)
		{
			notReported.Add(await harness.ReadAsync(device, slot));
		}

		var report = CompanionHarness.Report();
		report.InFocus = true;
		report.NetworkType = "wifi";
		report.NetworkMetered = false;
		report.NetworkValidated = true;
		report.NetworkName = "Studio";
		report.CpuUsagePercent = 12;
		report.MemoryUsedPercent = 34;
		await harness.ReportAsync("connection-1", device, report);
		var reported = new List<object?>();
		foreach (var slot in slots)
		{
			reported.Add((await harness.ReadAsync(device, slot)).Value);
		}

		harness.DeviceRegistry.Disconnected("connection-1");
		var disconnected = new List<VariableReading>();
		foreach (var slot in slots)
		{
			disconnected.Add(await harness.ReadAsync(device, slot));
		}

		Assert.Multiple(() =>
		{
			Assert.That(notReported, Is.All.SameAs(VariableReading.Unavailable));
			Assert.That(reported, Is.EqualTo(new object?[] { true, "wifi", false, true, "Studio", 12, 34 }));
			Assert.That(disconnected, Is.All.SameAs(VariableReading.Unavailable));
		});
	}

	[TestCase("screen-on", "screenOn")]
	[TestCase("screen-off", "screenOff")]
	[TestCase("focus-host", "focus")]
	public async Task A_requestable_control_is_sent_with_a_request_id_and_succeeds_when_the_device_is_done(
		string actionId,
		string capability)
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, capability);

		var running = CompanionStateAndActionsTests.ExecuteAsync(harness, actionId, device);
		var command = SentCommand(harness);
		var waitedForAnswer = !running.IsCompleted;
		Controller(harness, device).Claim(command.RequestId!);
		var done = Controller(harness, device).Done(command.RequestId!);
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
		var again = Controller(harness, device).Done(command.RequestId!);

		Assert.Multiple(() =>
		{
			Assert.That(command.Command, Is.EqualTo(capability));
			Assert.That(command.RequestId, Is.Not.Empty);
			Assert.That(waitedForAnswer, Is.True);
			Assert.That(done, Is.InstanceOf<NoContentResult>());
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(again, Is.InstanceOf<NotFoundResult>());
			Assert.That(harness.Transport.GroupMessages, Has.Count.EqualTo(1), "no cancelRequest after an answer");
		});
	}

	[TestCase("consentDenied", ActionErrorCodes.PermissionDenied)]
	[TestCase("unavailable", ActionErrorCodes.Unavailable)]
	[TestCase("failed", ActionErrorCodes.ProviderError)]
	public async Task A_refused_prompt_fails_the_control_with_its_reason(string reason, string errorCode)
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenOff");

		var running = CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-off", device);
		var reply = Controller(harness, device)
			.CommandFailed(SentCommand(harness).RequestId!, new CompanionScreenshotFailedRequest { Reason = reason });
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		LocalizedText expected = reason switch
		{
			"consentDenied" => AppStrings.Integrations.Companion.Errors.PermissionDeclined(),
			"unavailable" => AppStrings.Integrations.Companion.Errors.ScreenOffUnavailable(),
			_ => AppStrings.Integrations.Companion.Errors.CommandFailed()
		};
		Assert.Multiple(() =>
		{
			Assert.That(reply, Is.InstanceOf<NoContentResult>());
			Assert.That(result.ErrorCode, Is.EqualTo(errorCode));
			Assert.That(result.ErrorMessage, Is.EqualTo(expected));
			Assert.That(harness.Transport.GroupMessages, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task An_unanswered_prompt_times_out_cancels_the_request_once_and_refuses_a_late_answer()
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "focus");

		var running = CompanionStateAndActionsTests.ExecuteAsync(harness, "focus-host", device);
		var requestId = SentCommand(harness).RequestId!;
		harness.Time.Advance(CompanionCommandRequests.Deadline + TimeSpan.FromSeconds(1));
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
		var late = Controller(harness, device).Done(requestId);

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.Timeout));
			Assert.That(result.ErrorMessage,
				Is.EqualTo((LocalizedText)AppStrings.Integrations.Companion.Errors.PermissionTimedOut()));
			AssertCancelledOnce(harness, requestId);
			Assert.That(late, Is.InstanceOf<NotFoundResult>());
		});
	}

	[Test]
	public async Task Cancelling_the_action_sends_one_cancel_request_and_keeps_the_cancellation()
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenOn");
		using var cancellation = new CancellationTokenSource();

		var running = Run(harness, "screen-on", device, cancellation.Token);
		var requestId = SentCommand(harness).RequestId!;
		await cancellation.CancelAsync();
		var thrown = Assert.CatchAsync<OperationCanceledException>(async () => await running);
		var late = Controller(harness, device).Done(requestId);

		Assert.Multiple(() =>
		{
			Assert.That(thrown, Is.Not.Null);
			AssertCancelledOnce(harness, requestId);
			Assert.That(late, Is.InstanceOf<NotFoundResult>());
		});
	}

	[Test]
	public async Task A_device_without_command_results_stays_fire_and_forget()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device, CompanionHarness.Report(capabilities: "screenOff"));

		var result = await CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-off", device)
			.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(SentCommand(harness).RequestId, Is.Null);
		});
	}

	[Test]
	public async Task A_held_control_on_a_device_with_command_results_waits_and_fails_with_the_device()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		var report = CompanionHarness.Report(capabilities: "screenOff");
		report.RequestableCapabilities = [];
		await harness.ReportAsync("connection-1", device, report);

		var running = CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-off", device);
		var requestId = SentCommand(harness).RequestId;
		var claim = Controller(harness, device).Claim(requestId!);
		Controller(harness, device)
			.CommandFailed(requestId!, new CompanionScreenshotFailedRequest { Reason = "failed" });
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(requestId, Is.Not.Empty);
			Assert.That(claim, Is.InstanceOf<NoContentResult>());
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
			Assert.That(result.ErrorMessage,
				Is.EqualTo((LocalizedText)AppStrings.Integrations.Companion.Errors.CommandFailed()));
		});
	}

	[Test]
	public async Task A_claimed_control_outlives_the_pending_deadline_and_succeeds_on_done()
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenOff");

		var running = CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-off", device);
		var requestId = SentCommand(harness).RequestId!;
		harness.Time.Advance(CompanionCommandRequests.Deadline - TimeSpan.FromSeconds(5));
		var claim = Controller(harness, device).Claim(requestId);
		var secondClaim = Controller(harness, device).Claim(requestId);
		harness.Time.Advance(TimeSpan.FromSeconds(6));
		var runningAfterPendingDeadline = !running.IsCompleted;
		var done = Controller(harness, device).Done(requestId);
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(claim, Is.InstanceOf<NoContentResult>());
			Assert.That(secondClaim, Is.InstanceOf<NotFoundResult>());
			Assert.That(runningAfterPendingDeadline, Is.True);
			Assert.That(done, Is.InstanceOf<NoContentResult>());
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(harness.Transport.GroupMessages,
				Has.Count.EqualTo(1),
				"no cancelRequest for a claimed request");
		});
	}

	[TestCase("deadline")]
	[TestCase("cancel")]
	[TestCase("disconnect")]
	public async Task A_claim_after_the_host_gave_up_is_not_found(string giveUp)
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenOn");
		using var cancellation = new CancellationTokenSource();

		var running = Run(harness, "screen-on", device, cancellation.Token);
		var requestId = SentCommand(harness).RequestId!;
		switch (giveUp)
		{
			case "deadline":
				harness.Time.Advance(CompanionCommandRequests.Deadline + TimeSpan.FromSeconds(1));
				break;
			case "cancel":
				await cancellation.CancelAsync();
				break;
			default:
				harness.DeviceRegistry.Disconnected("connection-1");
				break;
		}

		try
		{
			await running.WaitAsync(TimeSpan.FromSeconds(5));
		}
		catch (OperationCanceledException)
		{
		}

		var claim = Controller(harness, device).Claim(requestId);

		Assert.That(claim, Is.InstanceOf<NotFoundResult>());
	}

	[Test]
	public async Task An_offline_device_is_not_connected_for_every_device_control()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		var report = CompanionHarness.Report(capabilities: "screenOn");
		report.RequestableCapabilities = ["screenOff"];
		await harness.ReportAsync("connection-1", device, report);
		harness.DeviceRegistry.Disconnected("connection-1");

		var results = new[]
		{
			await CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-on", device),
			await CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-off", device),
			await CompanionStateAndActionsTests.ExecuteAsync(harness, "focus-host", device),
			await Screenshot(harness, device, TakeScreenshotAction.DeckMode),
			await Screenshot(harness, device, TakeScreenshotAction.FullMode)
		};

		Assert.Multiple(() =>
		{
			Assert.That(results.Select(result => result.ErrorCode), Is.All.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(harness.Transport.GroupMessages, Is.Empty);
		});
	}

	[Test]
	public async Task Claim_and_failed_answer_only_their_own_kind()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		var report = CompanionHarness.Report(capabilities: "screenshotDeck");
		report.RequestableCapabilities = ["screenOn"];
		await harness.ReportAsync("connection-1", device, report);
		var failed = new CompanionScreenshotFailedRequest { Reason = "failed" };

		var screenshot = Screenshot(harness, device, TakeScreenshotAction.DeckMode);
		var screenshotId = SentCommand(harness).RequestId!;
		var command = CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-on", device);
		var commandId = SentCommand(harness).RequestId!;
		var claimScreenshot = Controller(harness, device).ClaimScreenshot(screenshotId);
		var commandRouteForScreenshot = Controller(harness, device).CommandFailed(screenshotId, failed);
		var screenshotRouteForCommand = Controller(harness, device).ScreenshotFailed(commandId, failed);
		var completedByWrongKind = screenshot.IsCompleted || command.IsCompleted;
		var screenshotRoute = Controller(harness, device).ScreenshotFailed(screenshotId, failed);
		var commandRoute = Controller(harness, device).CommandFailed(commandId, failed);
		await Task.WhenAll(screenshot, command).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(claimScreenshot,
				Is.InstanceOf<NoContentResult>(),
				"a device that answers commands claims screenshots");
			Assert.That(commandRouteForScreenshot, Is.InstanceOf<NotFoundResult>());
			Assert.That(screenshotRouteForCommand, Is.InstanceOf<NotFoundResult>());
			Assert.That(completedByWrongKind, Is.False);
			Assert.That(screenshotRoute, Is.InstanceOf<NoContentResult>());
			Assert.That(commandRoute, Is.InstanceOf<NoContentResult>());
		});
	}

	[Test]
	public async Task A_command_answer_from_another_device_is_not_found_and_completes_nothing()
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenOn");
		var other = harness.AddDevice("Tablet");
		await harness.ReportAsync("connection-2", other);

		var running = CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-on", device);
		var requestId = SentCommand(harness).RequestId!;
		var foreignDone = Controller(harness, other).Done(requestId);
		var foreignFailed = Controller(harness, other)
			.CommandFailed(requestId, new CompanionScreenshotFailedRequest { Reason = "failed" });
		var completedByForeignAnswer = running.IsCompleted;
		Controller(harness, device).Claim(requestId);
		Controller(harness, device).Done(requestId);
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(foreignDone, Is.InstanceOf<NotFoundResult>());
			Assert.That(foreignFailed, Is.InstanceOf<NotFoundResult>());
			Assert.That(completedByForeignAnswer, Is.False);
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		});
	}

	[Test]
	public async Task A_result_of_the_wrong_kind_is_not_found_and_completes_nothing()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		var report = CompanionHarness.Report(capabilities: "screenshotDeck");
		report.RequestableCapabilities = ["screenOn"];
		await harness.ReportAsync("connection-1", device, report);

		var screenshot = Screenshot(harness, device, TakeScreenshotAction.DeckMode);
		var screenshotId = SentCommand(harness).RequestId!;
		var command = CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-on", device);
		var commandId = SentCommand(harness).RequestId!;
		var doneForScreenshot = Controller(harness, device).Done(screenshotId);
		var pngForCommand = await Controller(harness, device, _png).Upload(commandId, CancellationToken.None);
		var completedByWrongKind = screenshot.IsCompleted || command.IsCompleted;
		Controller(harness, device).ClaimScreenshot(screenshotId);
		Controller(harness, device).Claim(commandId);
		await Controller(harness, device, _png).Upload(screenshotId, CancellationToken.None);
		Controller(harness, device).Done(commandId);
		var screenshotResult = await screenshot.WaitAsync(TimeSpan.FromSeconds(5));
		var commandResult = await command.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(doneForScreenshot, Is.InstanceOf<NotFoundResult>());
			Assert.That(pngForCommand, Is.InstanceOf<NotFoundResult>());
			Assert.That(completedByWrongKind, Is.False);
			Assert.That(screenshotResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(commandResult.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		});
	}

	[Test]
	public async Task Closing_the_last_connection_fails_a_pending_prompt_as_not_connected_without_a_cancel()
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenOff");

		var running = CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-off", device);
		harness.DeviceRegistry.Disconnected("connection-1");
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConnected));
			Assert.That(harness.Transport.GroupMessages, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_requestable_full_screenshot_waits_for_the_prompt_and_then_accepts_the_upload()
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenshotFull");

		var running = Screenshot(harness, device, TakeScreenshotAction.FullMode);
		var command = SentCommand(harness);
		var waitedForUpload = !running.IsCompleted;
		var uploadBeforeClaim = await Controller(harness, device, _png)
			.Upload(command.RequestId!, CancellationToken.None);
		var claim = Controller(harness, device).ClaimScreenshot(command.RequestId!);
		var upload = await Controller(harness, device, _png).Upload(command.RequestId!, CancellationToken.None);
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(command.ScreenshotMode, Is.EqualTo("full"));
			Assert.That(command.RequestId, Is.Not.Empty);
			Assert.That(waitedForUpload, Is.True);
			Assert.That(uploadBeforeClaim, Is.InstanceOf<NotFoundResult>());
			Assert.That(claim, Is.InstanceOf<NoContentResult>());
			Assert.That(upload, Is.InstanceOf<NoContentResult>());
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		});
	}

	[Test]
	public async Task A_declined_full_screenshot_prompt_fails_with_the_permission_text()
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenshotFull");

		var running = Screenshot(harness, device, TakeScreenshotAction.FullMode);
		Controller(harness, device).ScreenshotFailed(SentCommand(harness).RequestId!,
			new CompanionScreenshotFailedRequest { Reason = "consentDenied" });
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.PermissionDenied));
			Assert.That(result.ErrorMessage,
				Is.EqualTo((LocalizedText)AppStrings.Integrations.Companion.Errors.PermissionDeclined()));
		});
	}

	[Test]
	public async Task A_report_keeps_only_requestable_capabilities_the_device_does_not_hold()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		using var dispatcher = CompanionStateAndActionsTests.Dispatcher(harness,
			new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthDefaults.DeviceClaim, device.ToString())], "test")));

		await dispatcher.DispatchAsync("ReportCompanionState",
			CompanionStateAndActionsTests.Payload(new
			{
				capabilities = new[] { "focus" },
				requestableCapabilities = new[]
				{
					"screenOn", "screenOn", "focus", "screenshotDeck", "memory", "rootShell", "screenOff",
					"screenshotFull"
				}
			}),
			CancellationToken.None);
		await harness.DeviceRegistry.CreationFor(device).WaitAsync(TimeSpan.FromSeconds(5));

		harness.DeviceRegistry.TryGetState(device, out var state);
		Assert.That(state.RequestableCapabilities,
			Is.EquivalentTo(new[] { "screenOn", "screenOff", "screenshotFull" }));
	}

	[TestCase("cancel")]
	[TestCase("disconnect")]
	[TestCase("removal")]
	[TestCase("deadline")]
	public async Task A_claimed_control_succeeds_as_unconfirmed_when_the_host_would_otherwise_give_up(string giveUp)
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenOff");
		using var cancellation = new CancellationTokenSource();

		var running = Run(harness, "screen-off", device, cancellation.Token);
		Controller(harness, device).Claim(SentCommand(harness).RequestId!);
		switch (giveUp)
		{
			case "cancel":
				await cancellation.CancelAsync();
				harness.Time.Advance(CompanionCommandRequests.ClaimedCommandDeadline + TimeSpan.FromSeconds(1));
				break;
			case "disconnect":
				harness.DeviceRegistry.Disconnected("connection-1");
				break;
			case "removal":
				await harness.DeviceRegistry.RemoveDeviceAsync(device, CancellationToken.None);
				break;
			default:
				harness.Time.Advance(CompanionCommandRequests.ClaimedCommandDeadline + TimeSpan.FromSeconds(1));
				break;
		}

		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(harness.Transport.GroupMessages, Has.Count.EqualTo(1), "no cancelRequest after a claim");
		});
	}

	[Test]
	public async Task A_claimed_control_ignores_the_callers_cancellation_and_reports_the_devices_failure()
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "focus");
		using var cancellation = new CancellationTokenSource();

		var running = Run(harness, "focus-host", device, cancellation.Token);
		var requestId = SentCommand(harness).RequestId!;
		Controller(harness, device).Claim(requestId);
		await cancellation.CancelAsync();
		Controller(harness, device)
			.CommandFailed(requestId, new CompanionScreenshotFailedRequest { Reason = "failed" });
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.ProviderError));
			Assert.That(result.ErrorMessage,
				Is.EqualTo((LocalizedText)AppStrings.Integrations.Companion.Errors.CommandFailed()));
			Assert.That(harness.Transport.GroupMessages, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_done_racing_the_callers_cancellation_wins()
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenOn");
		using var cancellation = new CancellationTokenSource();
		harness.Transport.GroupSendGate = token => Task.Delay(Timeout.Infinite, token);

		var running = Run(harness, "screen-on", device, cancellation.Token);
		var requestId = SentCommand(harness).RequestId!;
		Controller(harness, device).Claim(requestId);
		var done = Controller(harness, device).Done(requestId);
		await cancellation.CancelAsync();
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(done, Is.InstanceOf<NoContentResult>());
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(harness.Transport.GroupMessages, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_claim_that_arrives_while_the_send_is_still_awaited_survives_the_callers_cancellation()
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenOff");
		using var cancellation = new CancellationTokenSource();
		harness.Transport.GroupSendGate = token => Task.Delay(Timeout.Infinite, token);

		var running = Run(harness, "screen-off", device, cancellation.Token);
		var requestId = SentCommand(harness).RequestId!;
		var claim = Controller(harness, device).Claim(requestId);
		await cancellation.CancelAsync();
		var stillWaiting = !running.IsCompleted;
		var done = Controller(harness, device).Done(requestId);
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(claim, Is.InstanceOf<NoContentResult>());
			Assert.That(stillWaiting, Is.True);
			Assert.That(done, Is.InstanceOf<NoContentResult>());
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
			Assert.That(harness.Transport.GroupMessages, Has.Count.EqualTo(1));
		});
	}

	[TestCase(true)]
	[TestCase(false)]
	public async Task A_send_that_fails_after_the_claim_still_waits_for_the_device(bool answered)
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenOff");
		var send = new TaskCompletionSource();
		harness.Transport.GroupSendGate = _ => send.Task;

		var running = CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-off", device);
		var requestId = SentCommand(harness).RequestId!;
		Controller(harness, device).Claim(requestId);
		send.SetException(new IOException("transport closed"));
		if (answered)
		{
			Controller(harness, device).Done(requestId);
		}
		else
		{
			harness.Time.Advance(CompanionCommandRequests.ClaimedCommandDeadline + TimeSpan.FromSeconds(1));
		}

		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
	}

	[Test]
	public async Task A_send_that_fails_before_the_claim_surfaces_the_error_and_gives_up()
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenOff");
		harness.Transport.GroupSendGate = _ => Task.FromException(new InvalidOperationException("transport closed"));

		var thrown = Assert.CatchAsync<InvalidOperationException>(async () =>
			await CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-off", device));
		var claim = Controller(harness, device).Claim(SentCommand(harness).RequestId!);

		Assert.Multiple(() =>
		{
			Assert.That(thrown, Is.Not.Null);
			Assert.That(claim, Is.InstanceOf<NotFoundResult>());
		});
	}

	[Test]
	public async Task A_claim_racing_a_failing_send_is_either_refused_or_honoured()
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenOff");
		var send = new TaskCompletionSource();
		harness.Transport.GroupSendGate = _ => send.Task;

		for (var attempt = 0; attempt < 200; attempt++)
		{
			send = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			var running = CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-off", device);
			var requestId = SentCommand(harness).RequestId!;
			using var start = new ManualResetEventSlim();
			var claim = Task.Run(() =>
			{
				start.Wait();
				return Controller(harness, device).Claim(requestId);
			});
			var fail = Task.Run(() =>
			{
				start.Wait();
				send.SetException(new InvalidOperationException("transport closed"));
			});
			start.Set();
			var claimed = await claim;
			await fail;

			if (claimed is NoContentResult)
			{
				Controller(harness, device).Done(requestId);
				var result = await running.WaitAsync(TimeSpan.FromSeconds(5));
				Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded), $"attempt {attempt}");
			}
			else
			{
				Assert.That(claimed, Is.InstanceOf<NotFoundResult>(), $"attempt {attempt}");
				Assert.CatchAsync<InvalidOperationException>(async () =>
						await running.WaitAsync(TimeSpan.FromSeconds(5)),
					$"attempt {attempt}");
			}
		}
	}

	[Test]
	public async Task Each_claim_route_accepts_only_its_own_kind()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		var report = CompanionHarness.Report(capabilities: "screenshotDeck");
		report.RequestableCapabilities = ["screenOn"];
		await harness.ReportAsync("connection-1", device, report);

		var screenshot = Screenshot(harness, device, TakeScreenshotAction.DeckMode);
		var screenshotId = SentCommand(harness).RequestId!;
		var command = CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-on", device);
		var commandId = SentCommand(harness).RequestId!;
		var commandRouteForScreenshot = Controller(harness, device).Claim(screenshotId);
		var screenshotRouteForCommand = Controller(harness, device).ClaimScreenshot(commandId);
		var commandRoute = Controller(harness, device).Claim(commandId);
		var screenshotRoute = Controller(harness, device).ClaimScreenshot(screenshotId);
		Controller(harness, device).Done(commandId);
		await Controller(harness, device, _png).Upload(screenshotId, CancellationToken.None);
		await Task.WhenAll(screenshot, command).WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(commandRouteForScreenshot, Is.InstanceOf<NotFoundResult>());
			Assert.That(screenshotRouteForCommand, Is.InstanceOf<NotFoundResult>());
			Assert.That(commandRoute, Is.InstanceOf<NoContentResult>());
			Assert.That(screenshotRoute, Is.InstanceOf<NoContentResult>());
		});
	}

	[Test]
	public async Task A_claimed_screenshot_has_the_upload_bound_to_arrive()
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenshotFull");

		var running = Screenshot(harness, device, TakeScreenshotAction.FullMode);
		var requestId = SentCommand(harness).RequestId!;
		Controller(harness, device).ClaimScreenshot(requestId);
		harness.Time.Advance(CompanionCommandRequests.ClaimedCommandDeadline + TimeSpan.FromSeconds(20));
		var waitingPastTheCommandDeadline = !running.IsCompleted;
		var upload = await Controller(harness, device, _png).Upload(requestId, CancellationToken.None);
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(CompanionCommandRequests.ClaimedScreenshotDeadline, Is.EqualTo(TimeSpan.FromSeconds(60)));
			Assert.That(waitingPastTheCommandDeadline, Is.True);
			Assert.That(upload, Is.InstanceOf<NoContentResult>());
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		});
	}

	[Test]
	public async Task An_upload_without_a_claim_is_not_found_for_a_device_that_answers_commands()
	{
		var harness = new CompanionHarness();
		var device = harness.AddDevice("Phone");
		var report = CompanionHarness.Report(capabilities: "screenshotDeck");
		report.RequestableCapabilities = [];
		await harness.ReportAsync("connection-1", device, report);

		var running = Screenshot(harness, device, TakeScreenshotAction.DeckMode);
		var upload = await Controller(harness, device, _png)
			.Upload(SentCommand(harness).RequestId!, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(upload, Is.InstanceOf<NotFoundResult>());
			Assert.That(running.IsCompleted, Is.False);
			Assert.That(Directory.Exists(_folder), Is.False);
		});
	}

	[Test]
	public async Task Done_without_a_claim_is_not_found_for_a_device_that_answers_commands()
	{
		var harness = new CompanionHarness();
		var device = await ConnectRequestableAsync(harness, "screenOn");

		var running = CompanionStateAndActionsTests.ExecuteAsync(harness, "screen-on", device);
		var requestId = SentCommand(harness).RequestId!;
		var unclaimed = Controller(harness, device).Done(requestId);
		var completedByUnclaimedDone = running.IsCompleted;
		Controller(harness, device).Claim(requestId);
		var claimed = Controller(harness, device).Done(requestId);
		var result = await running.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(unclaimed, Is.InstanceOf<NotFoundResult>());
			Assert.That(completedByUnclaimedDone, Is.False);
			Assert.That(claimed, Is.InstanceOf<NoContentResult>());
			Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Succeeded));
		});
	}

	private static void AssertCancelledOnce(CompanionHarness harness, string requestId)
	{
		var cancels = harness.Transport.GroupMessages
			.Select((sent, index) => (Command: (CompanionCommandEvent)sent.Message,
				Token: harness.Transport.GroupTokens[index]))
			.Where(sent => sent.Command.Command == "cancelRequest")
			.ToList();
		Assert.That(cancels, Has.Count.EqualTo(1));
		Assert.That(cancels[0].Command.RequestId, Is.EqualTo(requestId));
		Assert.That(cancels[0].Token.IsCancellationRequested, Is.False);
	}

	private static async Task<Guid> ConnectRequestableAsync(CompanionHarness harness, string capability)
	{
		var device = harness.AddDevice("Phone");
		var report = CompanionHarness.Report();
		report.RequestableCapabilities = [capability];
		await harness.ReportAsync("connection-1", device, report);
		return device;
	}

	private static Task<ActionResult> Run(CompanionHarness harness,
		string actionId,
		Guid device,
		CancellationToken cancellationToken)
		=> harness.Integration.Actions.Single(candidate => candidate.Id == actionId)
			.CreateExecutor()
			.ExecuteAsync(new ActionExecutionContext
			{
				Parameters = new Dictionary<string, object>
					{ [CompanionTargetResolver.ConfigurationParameter] = device.ToString("D") },
				CancellationToken = cancellationToken
			});

	private static async Task<Guid> ConnectAsync(CompanionHarness harness, string capability)
	{
		var device = harness.AddDevice("Phone");
		await harness.ReportAsync("connection-1", device, CompanionHarness.Report(capabilities: capability));
		return device;
	}

	private Task<ActionResult> Screenshot(CompanionHarness harness, Guid device, string mode, string? variable = null)
		=> CompanionStateAndActionsTests.ExecuteAsync(harness,
			"take-screenshot",
			device,
			(TakeScreenshotAction.ModeParameter, mode),
			(TakeScreenshotAction.FolderParameter, _folder),
			(TakeScreenshotAction.FileNameVariableParameter, variable ?? string.Empty));

	private static CompanionCommandEvent SentCommand(CompanionHarness harness)
		=> (CompanionCommandEvent)harness.Transport.GroupMessages.Last().Message;

	private static CompanionScreenshotsController Controller(CompanionHarness harness, Guid device, byte[]? body = null)
	{
		var context = new DefaultHttpContext
		{
			User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(AuthDefaults.DeviceClaim, device.ToString())],
				"test"))
		};
		context.Request.Body = new MemoryStream(body ?? []);
		return new CompanionScreenshotsController(harness.Requests)
		{
			ControllerContext = new ControllerContext { HttpContext = context }
		};
	}
}
