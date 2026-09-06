using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeckHost.Api.Plugins;
using MacroDeckHost.Application.Plugins.Pairing;
using MacroDeckHost.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

[ApiController]
[Route(ProtocolConstants.ProtocolDiscoveryPath)]
public class PluginProtocolController : ControllerBase
{
	private readonly PluginPairingOptions _pairingOptions;
	private readonly IAppPreferenceService _preferences;

	public PluginProtocolController(PluginPairingOptions pairingOptions, IAppPreferenceService preferences)
	{
		_pairingOptions = pairingOptions;
		_preferences = preferences;
	}

	[HttpGet]
	[AllowAnonymous]
	public async Task<IActionResult> Get()
	{
		if (PluginEndpointGate.TryReject(HttpContext, out var rejection))
		{
			return rejection!;
		}

		// Read per request rather than captured anywhere: Developer Mode is a live kill switch, and a
		// caller polling this endpoint is exactly how it learns the switch was flipped without a restart.
		var developer = await _preferences.GetDeveloper();

		var descriptor = ProtocolDescriptorFactory.CreateProtocolDescriptor() with
		{
			Pairing = new PluginPairingDescriptor
			{
				Supported = true,
				RequestLifetimeSeconds = (int)_pairingOptions.RequestLifetime.TotalSeconds,
				PollIntervalSeconds = (int)_pairingOptions.PollInterval.TotalSeconds,
				DeveloperModeEnabled = developer.Enabled
			},
			Enrollment = new PluginEnrollmentDescriptor { DeveloperModeEnabled = developer.Enabled }
		};

		return PluginProtocolHttp.Json(descriptor, StatusCodes.Status200OK);
	}
}
