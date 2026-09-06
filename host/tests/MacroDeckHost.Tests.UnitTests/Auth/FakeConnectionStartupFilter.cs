using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Auth;

public enum FakeConnectionShape
{
	Loopback,

	PublicLan,

	LoopbackOnPublicPort,

	LanOnPrivatePort
}

public sealed class FakeConnectionStartupFilter : IStartupFilter
{
	public const string ShapeHeader = "X-Test-Connection-Shape";

	public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
		=> app =>
		{
			app.Use(async (context, nextMiddleware) =>
			{
				var shape = context.Request.Headers.TryGetValue(ShapeHeader, out var value) &&
					Enum.TryParse<FakeConnectionShape>(value, out var parsed)
						? parsed
						: FakeConnectionShape.PublicLan;

				switch (shape)
				{
					case FakeConnectionShape.Loopback:
						context.Connection.LocalPort = TestListenerPorts.Loopback;
						context.Connection.RemoteIpAddress = IPAddress.Loopback;
						break;

					case FakeConnectionShape.LoopbackOnPublicPort:
						context.Connection.LocalPort = HostEndpoints.PublicPort;
						context.Connection.RemoteIpAddress = IPAddress.Loopback;
						break;

					case FakeConnectionShape.LanOnPrivatePort:
						context.Connection.LocalPort = TestListenerPorts.Loopback;
						context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
						break;

					default:
						context.Connection.LocalPort = HostEndpoints.PublicPort;
						context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.50");
						break;
				}

				await nextMiddleware();
			});
			next(app);
		};
}
