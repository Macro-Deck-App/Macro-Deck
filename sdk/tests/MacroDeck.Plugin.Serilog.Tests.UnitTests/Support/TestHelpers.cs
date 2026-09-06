namespace MacroDeck.Plugin.Serilog.Tests.UnitTests.Support;

/// <summary>Shared waiting helper for tests that observe work happening on a background loop.</summary>
internal static class TestHelpers
{
	/// <summary>Polls a condition with a deadline, so a failure is a failed assertion and not a hang.</summary>
	public static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(10);
		}

		Assert.Fail("The condition was never met.");
	}
}
