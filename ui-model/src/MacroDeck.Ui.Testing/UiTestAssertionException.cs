namespace MacroDeck.Ui.Testing;

/// <summary>
/// Thrown when the test host can already tell that what a test asked for is not what the view produced: a
/// query naming a node the tree does not have, a patch the producer emitted that its own applier rejects, or a
/// handler that faulted while the host was settling.
///
/// <para>
/// This package deliberately ships <b>no assertion helpers</b> and references no assertion library. A test
/// framework's own constraints already say everything about a value that is returned; what they cannot say is
/// "the thing you are about to assert on does not exist", and that is the only claim this exception makes.
/// </para>
/// </summary>
public sealed class UiTestAssertionException : Exception
{
	/// <summary>Creates the exception with no message.</summary>
	public UiTestAssertionException()
	{
	}

	/// <summary>Creates the exception carrying <paramref name="message" />.</summary>
	public UiTestAssertionException(string message)
		: base(message)
	{
	}

	/// <summary>Creates the exception carrying <paramref name="message" /> and
	/// <paramref name="innerException" />.</summary>
	public UiTestAssertionException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
