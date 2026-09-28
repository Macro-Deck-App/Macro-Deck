using System.Net;
using System.Security.Claims;
using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Usb;
using MacroDeckHost.Auth;

namespace MacroDeckHost.Ui;

public static class VideoStreamConsumerClassifier
{
	public static VideoStreamConsumer Classify(HttpContext context, ClaimsPrincipal principal)
		=> new(principal.FindFirst(AuthDefaults.DeviceClaim)?.Value,
			HostAddress(context.Request),
			ConnectionKind(context));

	public static VideoStreamConnectionKind ConnectionKind(HttpContext context)
	{
		var remote = context.Connection.RemoteIpAddress;
		var loopbackListener = LoopbackConnection.IsLoopbackListener(context);

		// The USB bridge forwards device traffic from loopback onto the public listener.
		if (context.Features.Get<IBridgedConnectionFeature>() is not null ||
			(remote is not null && IPAddress.IsLoopback(remote) && !loopbackListener))
		{
			return VideoStreamConnectionKind.UsbTunnel;
		}

		return loopbackListener && LoopbackConnection.IsLocalRequest(context)
			? VideoStreamConnectionKind.Local
			: VideoStreamConnectionKind.Network;
	}

	private static Uri? HostAddress(HttpRequest request)
		=> request.Host.HasValue &&
			Uri.TryCreate($"{request.Scheme}://{request.Host.Value}", UriKind.Absolute, out var address)
				? address
				: null;
}
