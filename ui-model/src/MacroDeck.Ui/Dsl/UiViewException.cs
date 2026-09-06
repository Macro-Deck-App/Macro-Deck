namespace MacroDeck.Ui.Dsl;

/// <summary>
/// Thrown when an authored tree violates an identity rule - an invalid key or composed id, or a duplicate
/// node id anywhere in the tree, including inside a <see cref="UiElement.Fallback" /> subtree. This is the
/// only diagnostic a plugin author gets when a tree fails to materialize, so every message names the
/// offending key or id, and a duplicate names both declaration paths.
/// </summary>
public sealed class UiViewException : Exception
{
	/// <summary>Creates the exception with a message naming the offending key or id.</summary>
	public UiViewException(string message)
		: base(message)
	{
	}

	/// <summary>Creates the exception with a message and an inner exception.</summary>
	public UiViewException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}
