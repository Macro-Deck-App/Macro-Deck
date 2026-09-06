using MacroDeckHost.Integrations.Native;

namespace MacroDeckHost.Tests.UnitTests.Mouse;

public class Win32AbsoluteCoordinatesTests
{
	private static int RoundTrip(int offset, int extent)
		=> (int)(((long)Win32AbsoluteCoordinates.Normalize(offset, extent) * extent) >> 16);

	[TestCase(1920)]
	[TestCase(1080)]
	[TestCase(2560)]
	[TestCase(1366)]
	[TestCase(3840)]
	[TestCase(5360)]
	public void Every_pixel_survives_the_round_trip(int extent)
	{
		var wrong = new List<int>();
		for (var offset = 0; offset < extent; offset++)
		{
			if (RoundTrip(offset, extent) != offset)
			{
				wrong.Add(offset);
			}
		}

		Assert.That(wrong, Is.Empty, $"{wrong.Count} of {extent} offsets did not survive the round trip");
	}

	[Test]
	public void A_coordinate_maps_back_to_itself_and_not_one_short()
	{
		Assert.Multiple(() =>
		{
			Assert.That(RoundTrip(100, 1920), Is.EqualTo(100));
			Assert.That(RoundTrip(100, 1080), Is.EqualTo(100));
		});
	}

	[Test]
	public void The_extremes_map_to_the_edges_of_the_normalized_range()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Win32AbsoluteCoordinates.Normalize(0, 1920), Is.EqualTo(17));
			Assert.That(Win32AbsoluteCoordinates.Normalize(1919, 1920), Is.LessThanOrEqualTo(65535));
			Assert.That(RoundTrip(1919, 1920), Is.EqualTo(1919));
		});
	}

	[Test]
	public void The_result_never_leaves_the_normalized_range()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Win32AbsoluteCoordinates.Normalize(-5000, 1920), Is.EqualTo(0));
			Assert.That(Win32AbsoluteCoordinates.Normalize(999_999, 1920), Is.EqualTo(65535));
		});
	}

	[Test]
	public void A_zero_or_negative_extent_is_a_safe_zero()
	{
		Assert.Multiple(() =>
		{
			Assert.That(Win32AbsoluteCoordinates.Normalize(100, 0), Is.EqualTo(0));
			Assert.That(Win32AbsoluteCoordinates.Normalize(100, -1), Is.EqualTo(0));
		});
	}

	[Test]
	public void A_wide_virtual_desktop_does_not_overflow_the_intermediate()
	{
		Assert.That(RoundTrip(40_000, 46_080), Is.EqualTo(40_000));
	}
}
