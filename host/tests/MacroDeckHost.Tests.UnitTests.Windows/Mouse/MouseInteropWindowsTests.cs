using MacroDeckHost.Integrations.Mouse;
using MacroDeckHost.Integrations.Mouse.Native;

namespace MacroDeckHost.Tests.UnitTests.Windows.Mouse;

[Platform("Win")]
public class MouseInteropWindowsTests
{
	private static IMouseInputProvider Provider() => MouseInputProviderFactory.Create();

	[Test]
	public void The_factory_produces_a_supported_provider_needing_no_permission()
	{
		var provider = Provider();

		Assert.Multiple(() =>
		{
			Assert.That(provider.IsSupported, Is.True);
			Assert.That(provider.RequiresPermission, Is.False);
			Assert.That(provider.HasPermission, Is.True);
			Assert.That(provider.IsDegraded, Is.False);
		});
	}

	[Test]
	public void Reading_the_cursor_marshals_the_POINT_out_parameter()
	{
		var provider = Provider();
		var read = false;
		var position = default(MousePoint);

		Assert.DoesNotThrow(() => read = provider.TryGetPosition(out position));
		TestContext.Out.WriteLine($"Cursor: {(read ? $"{position.X},{position.Y}" : "<unavailable>")}");
	}

	[Test]
	public void The_virtual_desktop_metrics_are_readable_and_have_a_positive_extent()
	{
		var provider = Provider();
		var read = false;
		var bounds = default(MouseRect);

		Assert.DoesNotThrow(() => read = provider.TryGetDesktopBounds(out bounds));
		TestContext.Out.WriteLine(read
			? $"Virtual desktop: left={bounds.Left} top={bounds.Top} {bounds.Width}x{bounds.Height}"
			: "Virtual desktop: <unavailable>");

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
	public async Task Moving_the_cursor_round_trips_to_the_requested_position()
	{
		var provider = Provider();
		if (!provider.TryGetPosition(out var original) || !provider.TryGetDesktopBounds(out var bounds))
		{
			Assert.Ignore("No interactive session; skipping the cursor round trip.");
			return;
		}

		var service = new MouseInputService(provider);
		var target = new MousePoint(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);

		try
		{
			await service.MoveAsync(MouseTarget.At(target.X, target.Y));
			await Task.Delay(150);

			Assert.That(provider.TryGetPosition(out var actual), Is.True);

			Assert.That(actual, Is.EqualTo(target));
		}
		finally
		{
			await service.MoveAsync(MouseTarget.At(original.X, original.Y));
		}
	}
}
