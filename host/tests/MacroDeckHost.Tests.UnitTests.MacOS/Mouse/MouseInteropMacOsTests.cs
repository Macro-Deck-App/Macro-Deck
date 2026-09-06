using MacroDeckHost.Integrations.Mouse;
using MacroDeckHost.Integrations.Mouse.Native;

namespace MacroDeckHost.Tests.UnitTests.MacOS.Mouse;

[Platform("MacOsX")]
public class MouseInteropMacOsTests
{
	private static IMouseInputProvider Provider() => MouseInputProviderFactory.Create();

	private static void RequireAccessibility(IMouseInputProvider provider)
	{
		if (!provider.HasPermission)
		{
			Assert.Ignore("Accessibility permission is not granted; skipping anything that posts an event.");
		}
	}

	[Test]
	public void The_factory_produces_a_supported_provider()
	{
		var provider = Provider();

		Assert.Multiple(() =>
		{
			Assert.That(provider.IsSupported, Is.True);
			Assert.That(provider.RequiresPermission, Is.True);
			Assert.That(provider.IsDegraded, Is.False);
		});
	}

	[Test]
	public void Reading_the_cursor_marshals_the_by_value_CGPoint_return()
	{
		var provider = Provider();
		var read = false;
		var position = default(MousePoint);

		Assert.DoesNotThrow(() => read = provider.TryGetPosition(out position));
		TestContext.Out.WriteLine($"Cursor: {(read ? $"{position.X},{position.Y}" : "<unavailable>")}");
	}

	[Test]
	public void Reading_the_desktop_bounds_marshals_the_by_value_CGRect_return()
	{
		var provider = Provider();
		var read = false;
		var bounds = default(MouseRect);

		Assert.DoesNotThrow(() => read = provider.TryGetDesktopBounds(out bounds));
		TestContext.Out.WriteLine(read
			? $"Desktop: left={bounds.Left} top={bounds.Top} {bounds.Width}x{bounds.Height}"
			: "Desktop: <unavailable>");

		if (read)
		{
			Assert.Multiple(() =>
			{
				Assert.That(bounds.Width, Is.GreaterThan(0));
				Assert.That(bounds.Height, Is.GreaterThan(0));
			});
		}
	}

	[Test]
	public void Releasing_nothing_is_safe()
	{
		var service = new MouseInputService(Provider());

		Assert.DoesNotThrowAsync(async () => await service.ReleaseAllAsync());
		Assert.That(service.HasHeldButtons, Is.False);
	}

	[Test]
	public void A_zero_notch_scroll_resolves_the_scroll_wheel_entry_point_without_posting()
	{
		var service = new MouseInputService(Provider());

		Assert.DoesNotThrowAsync(async () => await service.ScrollAsync(ScrollAxis.Vertical, 0));
	}

	[Test]
	[Explicit("Drives the real cursor and races whoever is using it; run it deliberately.")]
	public async Task Moving_the_cursor_round_trips_to_the_requested_position()
	{
		var provider = Provider();
		RequireAccessibility(provider);

		if (!provider.TryGetPosition(out var original) || !provider.TryGetDesktopBounds(out var bounds))
		{
			Assert.Ignore("No cursor or desktop bounds available in this session.");
			return;
		}

		var service = new MouseInputService(provider);
		var target = new MousePoint(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

		try
		{
			await service.MoveAsync(MouseTarget.At(target.X, target.Y));

			var actual = await SettleAtAsync(provider, target);

			Assert.That(actual, Is.EqualTo(target));
		}
		finally
		{
			await service.MoveAsync(MouseTarget.At(original.X, original.Y));
		}
	}

	private static async Task<MousePoint> SettleAtAsync(IMouseInputProvider provider, MousePoint target)
	{
		var position = default(MousePoint);
		var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);

		do
		{
			Assert.That(provider.TryGetPosition(out position), Is.True);
			if (position == target)
			{
				return position;
			}

			await Task.Delay(20);
		} while (DateTime.UtcNow < deadline);

		return position;
	}
}
