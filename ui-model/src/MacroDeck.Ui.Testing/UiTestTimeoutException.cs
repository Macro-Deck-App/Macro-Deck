namespace MacroDeck.Ui.Testing;

/// <summary>
/// Thrown by <see cref="UiTestHost.SettleAsync" /> when the view is still busy once the settle budget is
/// spent.
///
/// <para>
/// A distinct type rather than a bare <see cref="TimeoutException" /> or an
/// <see cref="OperationCanceledException" />: a stuck loader is the failure this exception exists to report,
/// and a test has to be able to tell it apart from the cancellation its own test-runner token produces or from
/// a timeout the code under test threw itself. The runtime owns no clock, so bounding the wait is the test
/// host's business - never the caller's, and never a poll loop.
/// </para>
/// </summary>
public sealed class UiTestTimeoutException : Exception
{
	/// <summary>Creates the exception with no message.</summary>
	public UiTestTimeoutException()
	{
	}

	/// <summary>Creates the exception carrying <paramref name="message" />.</summary>
	public UiTestTimeoutException(string message)
		: base(message)
	{
	}

	/// <summary>Creates the exception carrying <paramref name="message" /> and
	/// <paramref name="innerException" />.</summary>
	public UiTestTimeoutException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
