using System.Net;
using MacroDeckHost.Application.Usb;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace MacroDeckHost.Auth;

// A connection a USB link dialled arrives from 127.0.0.1 like a local client. The stamp is taken at accept
// and lives exactly as long as the Kestrel connection, so every request on it stays bridged (ADR 0095).
public static class BridgedConnectionStamp
{
	public static ListenOptions UseBridgedConnectionStamp(this ListenOptions listen, BridgedConnections registry)
	{
		listen.Use(next => context =>
		{
			Stamp(context, registry);
			return next(context);
		});
		return listen;
	}

	public static void Stamp(ConnectionContext context, BridgedConnections registry)
	{
		if (registry.Take(context.RemoteEndPoint as IPEndPoint) is { } deviceKey)
		{
			context.Features.Set<IBridgedConnectionFeature>(new BridgedConnectionFeature(deviceKey));
		}
	}

	public static string? DeviceKey(HttpContext context)
		=> context.Features.Get<IBridgedConnectionFeature>()?.DeviceKey;

	public static string MaskedDevice(string deviceKey)
	{
		var serial = deviceKey.StartsWith("usb:", StringComparison.Ordinal) ? deviceKey[4..] : deviceKey;
		var visible = serial.Length <= 4 ? serial : serial[^4..];
		return new string('\u2022', serial.Length - visible.Length) + visible;
	}

	public static string ClientKey(HttpContext context)
		=> DeviceKey(context) ?? context.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
}
