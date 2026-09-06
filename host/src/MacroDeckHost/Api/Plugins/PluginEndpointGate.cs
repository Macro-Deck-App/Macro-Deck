using System.Globalization;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Auth;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Plugins;

public static class PluginEndpointGate
{
	public static bool TryReject(HttpContext context, out IActionResult? rejection)
	{
		if (!LoopbackConnection.IsLocalRequest(context) || PluginBrowserGuard.IsBrowserRequest(context))
		{
			rejection = PluginProtocolHttp.Error(PluginErrors.Forbidden(), StatusCodes.Status403Forbidden);
			return true;
		}

		rejection = null;
		return false;
	}

	public static bool TryThrottle(LoginThrottle throttle,
		string key,
		HttpResponse response,
		out IActionResult? rejection)
	{
		if (throttle.IsThrottled(key, out var retryAfter))
		{
			response.Headers.RetryAfter
				= ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
			rejection = PluginProtocolHttp.Error(PluginErrors.RateLimited(retryAfter),
				StatusCodes.Status429TooManyRequests);
			return true;
		}

		rejection = null;
		return false;
	}
}
