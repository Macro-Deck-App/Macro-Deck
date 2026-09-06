using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.System;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Tests.UnitTests.Deck;

[TestFixture]
public class GetApplicationFocusCapabilityRequestMessageHandlerTests
{
	[Test]
	public async Task Handle_Supported_DoesNotStartASubscription()
	{
		var watcher = new FakeApplicationFocusWatcher { IsSupported = true };
		var handler = new GetApplicationFocusCapabilityRequestMessageHandler(watcher);

		var response = await handler.Handle(new GetApplicationFocusCapabilityRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Supported, Is.True);
			Assert.That(response.UnsupportedReason, Is.Null);
			Assert.That(watcher.WatchAsyncCalled, Is.False);
			Assert.That(response.PreferredIdentityKind,
				Is.EqualTo(OperatingSystem.IsMacOS()
					? ApplicationIdentityKind.BundleId
					: ApplicationIdentityKind.ExecutablePath));
		});
	}

	[Test]
	public async Task Handle_Unsupported_PassesTheReasonThroughVerbatimWithoutStartingASubscription()
	{
		var watcher = new FakeApplicationFocusWatcher { IsSupported = false, UnsupportedReason = "no X11 display" };
		var handler = new GetApplicationFocusCapabilityRequestMessageHandler(watcher);

		var response = await handler.Handle(new GetApplicationFocusCapabilityRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Supported, Is.False);
			Assert.That(response.UnsupportedReason, Is.EqualTo("no X11 display"));
			Assert.That(watcher.WatchAsyncCalled, Is.False);
		});
	}

	private sealed class FakeApplicationFocusWatcher : IApplicationFocusWatcher
	{
		public bool IsSupported { get; init; }

		public string? UnsupportedReason { get; init; }

		public bool WatchAsyncCalled { get; private set; }

		public IAsyncEnumerable<FocusedApplication> WatchAsync(CancellationToken cancellationToken)
		{
			WatchAsyncCalled = true;
			throw new InvalidOperationException("WatchAsync must not be called for a capability query");
		}
	}
}
