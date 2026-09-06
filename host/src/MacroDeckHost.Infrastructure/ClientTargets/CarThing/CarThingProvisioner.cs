using System.Net;
using System.Text.Json;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.ClientTargets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Infrastructure.ClientTargets.CarThing;

/// <summary>
/// Prepares a Spotify Car Thing to run the <c>carthing</c> Web Client target (issue #727).
///
/// The split between what this automates and what it only explains is deliberate. Flashing the device
/// needs a physical button combination and a third-party tool, and gets it wrong at the cost of a
/// brick - so that step is guided, never driven. Everything after it is ordinary adb work and is done
/// here: finding the device, confirming the reverse tunnel the host already maintains (ADR 0030), and
/// writing the address of the target build into the device's kiosk configuration.
/// </summary>
internal sealed class CarThingProvisioner : IWebClientTargetProvisioner
{
	internal const string TargetIdentifier = "carthing";

	internal const string StepDetect = "detect";
	internal const string StepPrepare = "prepare";
	internal const string StepConnect = "connect";
	internal const string StepConfigure = "configure";
	internal const string StepDone = "done";

	internal const string FieldWebRoot = "webRoot";
	internal const string FieldServiceManager = "serviceManager";
	internal const string FieldServiceName = "serviceName";

	/// <summary>
	/// The page the device's kiosk browser ends up on. Chromium is started with
	/// <c>--kiosk --app=http://localhost:8080</c> and a local web server answers that, so pointing the
	/// device at Macro Deck means replacing what that server returns - not editing supervisord, which
	/// is what an earlier attempt got wrong.
	/// </summary>
	private const string IndexFileName = "index.html";

	private const string FlashingToolUrl = "https://thingify.tools/";

	private readonly IAdbManager _adbManager;
	private readonly IFileSystemScratchWriter _scratch;
	private readonly IAuthService _authService;
	private readonly IServiceScopeFactory _scopeFactory;

	public CarThingProvisioner(
		IAdbManager adbManager,
		IFileSystemScratchWriter scratch,
		IAuthService authService,
		IServiceScopeFactory scopeFactory)
	{
		_adbManager = adbManager;
		_scratch = scratch;
		_authService = authService;
		_scopeFactory = scopeFactory;
	}

	public string TargetId => TargetIdentifier;

	public Task<bool> IsAvailableAsync(CancellationToken cancellationToken)
		=> Task.FromResult(_adbManager.Status is { Enabled: true, UsbConnectionsEnabled: true });

	public Task<WebClientTargetProvisioningResult> StartAsync(CancellationToken cancellationToken)
		=> Task.FromResult(WebClientTargetProvisioningResult.Next(DetectStep()));

	public async Task<WebClientTargetProvisioningResult> AdvanceAsync(
		string stepId,
		IReadOnlyDictionary<string, string> input,
		CancellationToken cancellationToken)
	{
		return stepId switch
		{
			StepDetect => AdvanceFromDetect(),
			StepPrepare => WebClientTargetProvisioningResult.Next(ConnectStep(FindReadyDevice())),
			StepConnect => await AdvanceFromConnectAsync(cancellationToken),
			StepConfigure => await ConfigureAsync(input, cancellationToken),
			// An unknown or stale step id restarts rather than throwing: a browser tab left open across
			// a host restart must not be able to drive this into a state it never reached.
			_ => WebClientTargetProvisioningResult.Next(DetectStep())
		};
	}

	/// <summary>Any attached Car Thing, whatever state adb has it in.</summary>
	private AdbDevice? FindDevice()
		=> _adbManager.Devices.FirstOrDefault(CarThingIdentity.Matches);

	/// <summary>The one that can actually be worked on.</summary>
	private AdbDevice? FindReadyDevice()
		=> _adbManager.Devices.FirstOrDefault(device => device.IsAuthorized && CarThingIdentity.Matches(device));

	private WebClientTargetProvisioningResult AdvanceFromDetect()
	{
		var device = FindDevice();
		if (device is null)
		{
			return WebClientTargetProvisioningResult.Failed(DetectStep(),
				AppStrings.ClientTargets.CarThing.Detect.NotFound());
		}

		// A device adb has listed but cannot talk to is a different problem from an absent one, and
		// "check the cable" sends the user looking in the wrong place for it.
		if (!device.IsAuthorized)
		{
			return WebClientTargetProvisioningResult.Failed(DetectStep(),
				AppStrings.ClientTargets.CarThing.Detect.NotReady(state: device.State.ToString()));
		}

		return WebClientTargetProvisioningResult.Next(PrepareStep());
	}

	private async Task<WebClientTargetProvisioningResult> AdvanceFromConnectAsync(
		CancellationToken cancellationToken)
	{
		var device = FindReadyDevice();
		if (device is null)
		{
			return WebClientTargetProvisioningResult.Failed(ConnectStep(null),
				AppStrings.ClientTargets.CarThing.Detect.NotFound());
		}

		if (device.Tunnel is not { Established: true })
		{
			return WebClientTargetProvisioningResult.Failed(ConnectStep(device),
				AppStrings.ClientTargets.CarThing.Connect.NoTunnel());
		}

		// Asking the device what it is beats asking the user: the fields below arrive filled in with
		// what this firmware actually uses, and stay editable for the variant nobody has seen.
		var firmware = await CarThingFirmwareDetector.DetectAsync(_adbManager, device.Serial, cancellationToken);
		return WebClientTargetProvisioningResult.Next(ConfigureStep(device, firmware));
	}

	private async Task<WebClientTargetProvisioningResult> ConfigureAsync(
		IReadOnlyDictionary<string, string> input,
		CancellationToken cancellationToken)
	{
		var device = FindReadyDevice();
		if (device?.Tunnel is not { Established: true })
		{
			return await AdvanceFromConnectAsync(cancellationToken);
		}

		var firmware = CarThingFirmware.SupervisordKiosk;
		var webRoot = Read(input, FieldWebRoot, firmware.WebRoot)
			.TrimEnd('/');
		var serviceName = Read(input,
			FieldServiceName,
			firmware.KioskService);
		var manager = Read(input,
			FieldServiceManager,
			nameof(AdbServiceManager.Supervisord));

		if (!Enum.TryParse<AdbServiceManager>(manager, ignoreCase: true, out var serviceManager))
		{
			serviceManager = AdbServiceManager.Supervisord;
		}

		var clientUrl = ClientUrlFor(device.Tunnel.DevicePort);

		// The device has no keyboard, so nobody can sign it in. Setup runs on the loopback listener with
		// admin, which is the one position from which handing a device a single client-scope session is
		// legitimate. The credential goes in the fragment, never the query: it stays out of the host's
		// logs, and the client clears it the moment it is spent.
		var enrollment = await _authService.CreateDeviceEnrollment(AuthDefaults.DeviceEnrollmentLifetime);
		var pageUrl = enrollment is { Success: true, Data: { } ticket }
			? $"{clientUrl}#enroll={Uri.EscapeDataString(ticket.Token)}"
			: clientUrl;

		// The device's root filesystem is read-only until remounted. A firmware that already mounts it
		// writable simply answers non-zero here, so the push below is what decides whether the write
		// actually worked.
		await _adbManager.ExecuteAsync(new AdbRemountRootWritableCommand(device.Serial), cancellationToken);

		var indexPath = $"{webRoot}/{IndexFileName}";
		var waitingText = await ActiveLocalization.Resolve(_scopeFactory,
			AppStrings.ClientTargets.CarThing.Waiting());
		var scratchFile = await _scratch.WriteAsync(RedirectPageFor(pageUrl, waitingText),
			cancellationToken);
		try
		{
			var push = await _adbManager.ExecuteAsync(new AdbPushFileCommand(device.Serial, scratchFile, indexPath),
				cancellationToken);
			if (!push.Success)
			{
				return WebClientTargetProvisioningResult.Failed(ConfigureStep(device, firmware),
					AppStrings.ClientTargets.CarThing.Configure.WriteFailed(path: indexPath));
			}
		}
		finally
		{
			_scratch.Delete(scratchFile);
		}

		var restart = await _adbManager.ExecuteAsync(
			new AdbRestartServiceCommand(device.Serial, serviceManager, serviceName),
			cancellationToken);
		if (!restart.Success)
		{
			// The address is written, so the device shows Macro Deck after its next restart either way.
			// Reporting this as a failure of the whole run would send the user back through steps that
			// already succeeded.
			return WebClientTargetProvisioningResult.Failed(DoneStep(clientUrl),
				AppStrings.ClientTargets.CarThing.Configure.RestartFailed(service: serviceName));
		}

		return WebClientTargetProvisioningResult.Done(DoneStep(clientUrl));
	}

	/// <summary>
	/// The page the device's kiosk browser lands on. It waits rather than jumping: the device boots
	/// faster than the host establishes its reverse tunnel, and a browser that navigates into a
	/// connection that does not exist yet stays on the failed page forever - a blank screen with no
	/// way back. So it polls the host and only navigates once there is something to navigate to, and
	/// says so on screen while it waits.
	///
	/// A redirect and not the client itself, because the host already serves the target build over the
	/// tunnel - and going there rather than being served locally keeps the client on the same origin
	/// as the API it talks to, which is what lets the session cookie work at all.
	/// </summary>
	internal static string RedirectPageFor(string clientUrl, string waitingText)
	{
		var origin = new Uri(clientUrl).GetLeftPart(UriPartial.Authority);
		var url = JavaScriptString(clientUrl);
		var probe = JavaScriptString($"{origin}/api/system/build-info");

		return "<!doctype html>\n" +
			"<meta charset=\"utf-8\">\n" +
			"<title>Macro Deck</title>\n" +
			"<style>html,body{height:100%;margin:0;background:#101014;color:#a0a0a8;" +
			"font-family:sans-serif;display:flex;align-items:center;justify-content:center}</style>\n" +
			$"<body><p>{WebUtility.HtmlEncode(waitingText)}</p>\n" +
			"<script>(function(){\n" +
			$"var url={url},probe={probe},tries=0;\n" +
			"function go(){location.replace(url);}\n" +
			"function attempt(){\n" +
			// Give up probing eventually and go anyway: a firmware whose browser blocks the
			// cross-origin probe would otherwise wait here forever with the host perfectly reachable.
			"if(++tries>60){go();return;}\n" +
			"var x=new XMLHttpRequest();x.open('GET',probe+'?t='+tries,true);x.timeout=2000;\n" +
			"x.onload=go;x.onerror=retry;x.ontimeout=retry;x.send();}\n" +
			"function retry(){setTimeout(attempt,1000);}\n" +
			"attempt();})();</script>\n";
	}

	/// <summary>Quotes a value for a JavaScript string literal, so a URL cannot end the script.</summary>
	private static string JavaScriptString(string value) => JsonSerializer.Serialize(value);

	internal static string ClientUrlFor(int devicePort)
		=> $"http://127.0.0.1:{devicePort}/targets/carthing/";

	private static string Read(IReadOnlyDictionary<string, string> input, string key, string fallback)
		=> input.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : fallback;

	private static WebClientTargetProvisioningStep DetectStep() => new()
	{
		StepId = StepDetect,
		Title = AppStrings.ClientTargets.CarThing.Detect.Title(),
		Description = AppStrings.ClientTargets.CarThing.Detect.Description(),
		Instructions =
		[
			AppStrings.ClientTargets.CarThing.Detect.ConnectUsb(),
			AppStrings.ClientTargets.CarThing.Detect.EnableAdb()
		]
	};

	private static WebClientTargetProvisioningStep PrepareStep() => new()
	{
		StepId = StepPrepare,
		Title = AppStrings.ClientTargets.CarThing.Prepare.Title(),
		Description = AppStrings.ClientTargets.CarThing.Prepare.Description(),
		Instructions =
		[
			AppStrings.ClientTargets.CarThing.Prepare.EnterUsbMode(),
			AppStrings.ClientTargets.CarThing.Prepare.RunFlashingTool(),
			AppStrings.ClientTargets.CarThing.Prepare.Reboot()
		],
		Links =
		[
			new WebClientTargetProvisioningLink(AppStrings.ClientTargets.CarThing.Prepare.ToolsLink(),
				FlashingToolUrl)
		]
	};

	private static WebClientTargetProvisioningStep ConnectStep(AdbDevice? device) => new()
	{
		StepId = StepConnect,
		Title = AppStrings.ClientTargets.CarThing.Connect.Title(),
		Description = AppStrings.ClientTargets.CarThing.Connect.Description(),
		Values = device?.Tunnel is { Established: true } tunnel
			?
			[
				new WebClientTargetProvisioningValue(AppStrings.ClientTargets.CarThing.Connect.AddressLabel(),
					ClientUrlFor(tunnel.DevicePort))
			]
			: []
	};

	private static WebClientTargetProvisioningStep ConfigureStep(AdbDevice device, CarThingFirmware firmware)
		=> new()
		{
			StepId = StepConfigure,
			Title = AppStrings.ClientTargets.CarThing.Configure.Title(),
			Description = AppStrings.ClientTargets.CarThing.Configure.Description(),
			Values =
			[
				new WebClientTargetProvisioningValue(AppStrings.ClientTargets.CarThing.Connect.AddressLabel(),
					ClientUrlFor(device.Tunnel!.DevicePort))
			],
			Fields =
			[
				new WebClientTargetProvisioningField(FieldWebRoot,
					AppStrings.ClientTargets.CarThing.Configure.PathLabel(),
					AppStrings.ClientTargets.CarThing.Configure.PathDescription(),
					firmware.WebRoot),
				new WebClientTargetProvisioningField(FieldServiceName,
					AppStrings.ClientTargets.CarThing.Configure.ServiceLabel(),
					AppStrings.ClientTargets.CarThing.Configure.ServiceDescription(),
					firmware.KioskService),
				new WebClientTargetProvisioningField(FieldServiceManager,
					AppStrings.ClientTargets.CarThing.Configure.ManagerLabel(),
					AppStrings.ClientTargets.CarThing.Configure.ManagerDescription(),
					firmware.ServiceManager.ToString(),
					[
						new WebClientTargetProvisioningChoice(nameof(AdbServiceManager.Supervisord),
							AppStrings.ClientTargets.CarThing.Configure.ManagerSupervisord()),
						new WebClientTargetProvisioningChoice(nameof(AdbServiceManager.Systemd),
							AppStrings.ClientTargets.CarThing.Configure.ManagerSystemd()),
						new WebClientTargetProvisioningChoice(nameof(AdbServiceManager.SysVInit),
							AppStrings.ClientTargets.CarThing.Configure.ManagerSysVInit())
					])
			]
		};

	private static WebClientTargetProvisioningStep DoneStep(string clientUrl) => new()
	{
		StepId = StepDone,
		Title = AppStrings.ClientTargets.CarThing.Done.Title(),
		Description = AppStrings.ClientTargets.CarThing.Done.Description(),
		Values =
		[
			new WebClientTargetProvisioningValue(AppStrings.ClientTargets.CarThing.Connect.AddressLabel(), clientUrl)
		],
		CanContinue = false
	};
}
