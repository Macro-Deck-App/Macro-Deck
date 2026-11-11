namespace MacroDeckHost.Tests.Support;

internal static class Interactive
{
	public static bool IsRequired => Environment.GetEnvironmentVariable("MACRODECK_REQUIRE_INTERACTIVE") == "1";

	public static void Require(string reason)
	{
		if (IsRequired)
		{
			Assert.Fail($"Required interactive session is missing: {reason}");
		}

		Assert.Ignore(reason);
	}

	public static void Require(bool condition, string reason)
	{
		if (!condition)
		{
			Require(reason);
		}
	}
}
