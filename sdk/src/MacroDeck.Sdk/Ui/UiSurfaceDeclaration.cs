namespace MacroDeck.Sdk.Ui;

/// <summary>One surface an <see cref="IUiProvider" /> is willing to serve.</summary>
/// <remarks>
/// A declaration, not a session: it carries no attributes, because those describe one place a tree is
/// rendered and only exist once the host opens a session. Both values are free strings - the surface
/// kind and session mode vocabularies are open, and a value this Macro Deck build does not recognise
/// travels unchanged rather than failing the declaration.
/// </remarks>
public sealed record UiSurfaceDeclaration
{
	/// <summary>What kind of surface, for example <c>config</c> or <c>widget</c>. Declaring a kind here
	/// does not commit the provider to serving it: <see cref="IUiProvider.CreateSessionAsync" /> may
	/// still decline an individual session.</summary>
	public required string Kind { get; init; }

	/// <summary>How many clients the provider expects to serve at once, for example <c>exclusive</c> or
	/// <c>shared</c>. The host enforces this; a mode it does not recognise is enforced as exclusive, so
	/// an unknown value never fans one tree out to several clients by accident.</summary>
	public required string SessionMode { get; init; }
}
