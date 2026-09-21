using MacroDeckHost.Application.Auth;
using MacroDeckHost.Infrastructure.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Auth;

public class LastServedRotationTests
{
	private static readonly DateTime RotatedAt = new(2026, 9, 13, 12, 0, 30, DateTimeKind.Utc);

	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp() => _paths = new TestPaths();

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_paths.BaseDirectory))
		{
			Directory.Delete(_paths.BaseDirectory, true);
		}
	}

	[Test]
	public void A_host_that_never_rotated_anything_leaves_nothing_to_read()
	{
		Assert.That(new LastServedRotation(_paths).Read(), Is.EqualTo(DateTime.MinValue));
	}

	[Test]
	public void What_one_host_recorded_is_what_the_next_one_reads()
	{
		new LastServedRotation(_paths).Record(RotatedAt);

		Assert.That(new LastServedRotation(_paths).Read(), Is.EqualTo(RotatedAt));
	}

	[Test]
	public void A_record_that_cannot_be_read_back_refuses_the_grace_rather_than_widening_it()
	{
		var record = new LastServedRotation(_paths);
		record.Record(RotatedAt);
		File.WriteAllText(Path.Combine(_paths.DataDirectory, "last-rotation"), "not a timestamp");

		Assert.That(record.Read(), Is.EqualTo(DateTime.MinValue));
	}

	[Test]
	public void Only_the_first_caller_sets_the_epoch()
	{
		var epoch = new RefreshServingEpoch();
		var first = epoch.Begin(RotatedAt.AddMinutes(1), RotatedAt);

		var second = epoch.Begin(RotatedAt.AddMinutes(2), RotatedAt.AddMinutes(2));

		Assert.Multiple(() =>
		{
			Assert.That(first, Is.True);
			Assert.That(second, Is.False);
			Assert.That(epoch.StartedAt, Is.EqualTo(RotatedAt.AddMinutes(1)));
			Assert.That(epoch.PreviousRotationAt, Is.EqualTo(RotatedAt));
		});
	}
}
