using MacroDeckHost.Tests.Support;

[assembly: NonParallelizable]
[assembly: LevelOfParallelism(1)]

namespace MacroDeckHost.Tests.PlatformInput;

[SetUpFixture]
public sealed class PlatformInputSetUp
{
	[OneTimeSetUp]
	public void RequireOptIn()
	{
		if (Environment.GetEnvironmentVariable("MACRODECK_PLATFORM_INPUT") != "1")
		{
			Assert.Ignore(
				"These tests inject real keyboard and mouse input; set MACRODECK_PLATFORM_INPUT=1 to run them.");
		}
	}

	[OneTimeTearDown]
	public void RemoveProbeCopies()
	{
		ProbeLocator.DeleteCopies();
	}
}
