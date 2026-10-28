using MacroDeckHost.Infrastructure.Secrets;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Secrets;

public class SecretLineageIndexTests
{
	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_paths.BaseDirectory))
		{
			Directory.Delete(_paths.BaseDirectory, true);
		}
	}

	[Test]
	public void A_clone_of_a_clone_still_traces_back_to_its_original_after_a_restart()
	{
		var original = Guid.NewGuid();
		var copy = Guid.NewGuid();
		var copyOfCopy = Guid.NewGuid();
		var running = new SecretLineageIndex(_paths, Log.Logger);
		running.RecordClone(original, copy);
		running.RecordClone(copy, copyOfCopy);

		var restarted = new SecretLineageIndex(_paths, Log.Logger);

		Assert.Multiple(() =>
		{
			Assert.That(restarted.Root(copyOfCopy), Is.EqualTo(original));
			Assert.That(restarted.Root(original), Is.EqualTo(original));
		});
	}
}
