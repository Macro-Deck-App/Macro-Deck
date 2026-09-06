using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.System;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Ui;

[TestFixture]
public class GetServerTimeRequestMessageHandlerTests
{
	[Test]
	public async Task Returns_the_host_clock_as_unix_milliseconds()
	{
		var time = new ManualTimeProvider { Now = new DateTimeOffset(2026, 7, 24, 20, 15, 30, 123, TimeSpan.Zero) };
		var handler = new GetServerTimeRequestMessageHandler(time);

		var response = await handler.Handle(new GetServerTimeRequest(), CancellationToken.None);

		Assert.That(response.UtcMs, Is.EqualTo(time.Now.ToUnixTimeMilliseconds()));
	}

	[Test]
	public async Task Follows_the_clock_between_calls()
	{
		var time = new ManualTimeProvider();
		var handler = new GetServerTimeRequestMessageHandler(time);

		var first = await handler.Handle(new GetServerTimeRequest(), CancellationToken.None);
		time.Advance(TimeSpan.FromMilliseconds(1500));
		var second = await handler.Handle(new GetServerTimeRequest(), CancellationToken.None);

		Assert.That(second.UtcMs - first.UtcMs, Is.EqualTo(1500));
	}
}
