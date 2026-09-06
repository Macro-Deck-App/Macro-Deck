namespace MacroDeck.Plugin.Testing;

/// <summary>
/// A test-side wait gave up before its condition became true. Thrown by <see cref="Wait.UntilAsync" />
/// and by the collectors' own <c>WaitForAsync</c> helpers - never by the plugin under test, and never
/// caught anywhere in this package, so a test sees it as a failure rather than a silent timeout.
/// </summary>
public sealed class PluginTestTimeoutException : TimeoutException
{
	/// <summary>Creates the exception with a message describing what was being waited for.</summary>
	public PluginTestTimeoutException(string message)
		: base(message)
	{
	}

	/// <summary>Creates the exception with a message and an inner exception.</summary>
	public PluginTestTimeoutException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	/// <summary>Creates the exception with a default message.</summary>
	public PluginTestTimeoutException()
		: base("The condition was not met before the deadline.")
	{
	}
}
