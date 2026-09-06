using MacroDeckHost.Integrations.Mouse;
using MacroDeckHost.Integrations.Mouse.Native;

namespace MacroDeckHost.Tests.UnitTests.Linux.Mouse;

[Platform("Linux")]
public class MouseInteropLinuxTests
{
	private static bool HasDisplay
		=> !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

	private static IMouseInputProvider Provider() => MouseInputProviderFactory.Create();

	[Test]
	public void The_factory_never_throws_even_without_a_display()
	{
		IMouseInputProvider? provider = null;

		Assert.DoesNotThrow(() => provider = Provider());
		Assert.That(provider, Is.Not.Null);
		TestContext.Out.WriteLine($"Provider: {provider!.PlatformName}, supported: {provider.IsSupported}");
	}

	[Test]
	public void An_unavailable_display_is_reported_rather_than_thrown()
	{
		var provider = Provider();
		if (HasDisplay)
		{
			Assert.Ignore("A display is reachable; this test covers the headless path.");
			return;
		}

		Assert.That(provider.IsSupported, Is.False);
		Assert.DoesNotThrow(() => provider.TryGetPosition(out _));
		Assert.DoesNotThrow(() => provider.TryGetDesktopBounds(out _));
	}

	[Test]
	public void Releasing_nothing_is_safe_even_when_unsupported()
	{
		var service = new MouseInputService(Provider());

		Assert.DoesNotThrowAsync(async () => await service.ReleaseAllAsync());
	}

	[Test]
	public void XWayland_is_reported_as_degraded_rather_than_unsupported()
	{
		var provider = Provider();
		if (!provider.IsSupported)
		{
			Assert.Ignore("No X display; nothing to classify.");
			return;
		}

		var wayland = string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
				"wayland",
				StringComparison.OrdinalIgnoreCase) ||
			!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

		TestContext.Out.WriteLine($"Wayland session: {wayland}, degraded: {provider.IsDegraded}");

		if (wayland)
		{
			Assert.Multiple(() =>
			{
				Assert.That(provider.IsDegraded, Is.True);
				Assert.That(provider.DegradedReason, Is.Not.Empty);
			});
		}
	}

	[Test]
	public void Querying_the_pointer_marshals_without_throwing()
	{
		var provider = Provider();
		if (!provider.IsSupported)
		{
			Assert.Ignore("No X display available.");
			return;
		}

		var read = false;
		var position = default(MousePoint);

		Assert.DoesNotThrow(() => read = provider.TryGetPosition(out position));
		TestContext.Out.WriteLine($"Pointer: {(read ? $"{position.X},{position.Y}" : "<unavailable>")}");
	}
}
