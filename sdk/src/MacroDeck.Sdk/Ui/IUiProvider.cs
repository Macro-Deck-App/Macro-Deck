namespace MacroDeck.Sdk.Ui;

/// <summary>
/// Implemented by integrations that serve Macro Deck UI trees. The host opens one session per place a
/// tree is rendered and routes patches and events between this provider and whichever clients attach.
/// </summary>
/// <remarks>
/// The host owns the session: it issues the id, enforces the limits, and decides when the session ends.
/// A provider is asked for a session, then driven through <see cref="IUiSession" /> until the host
/// disposes it.
/// </remarks>
public interface IUiProvider
{
	/// <summary>The surfaces this provider is willing to serve, as reported to Macro Deck when the
	/// provider is discovered.</summary>
	/// <remarks>
	/// Read before anything has been initialized and possibly again later, so it must be side-effect
	/// free and must not depend on a live connection or completed configuration. An empty list means
	/// the provider serves no surface at all, which Macro Deck reports rather than treating as an
	/// error.
	/// </remarks>
	IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; }

	/// <summary>
	/// Creates a session for the requested surface, or returns <c>null</c> to decline it - which is the
	/// correct answer for a surface kind this provider does not serve, and is not an error.
	/// </summary>
	/// <remarks>
	/// Called once per session. The returned session is owned by the host for the session's lifetime and
	/// is disposed when the session closes, including when the host tears it down after a fault.
	/// </remarks>
	Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken);
}
