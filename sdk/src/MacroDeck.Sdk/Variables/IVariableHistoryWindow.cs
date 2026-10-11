namespace MacroDeck.Sdk.Variables;

/// <summary>
/// A rolling window of recent values of one of the integration's own numeric variables, opened with
/// <see cref="IVariableApi.OpenHistoryAsync" />. Macro Deck samples the variable once per second while at
/// least one window on it is open, so the values are evenly spaced and a graph of them advances whether the
/// value changes or not. Dispose the window when it is no longer shown; that ends the hold on the sampling.
/// </summary>
public interface IVariableHistoryWindow : IAsyncDisposable
{
	/// <summary>
	/// The retained samples, oldest first, at most the capacity the window was opened with. Empty until the
	/// variable first reads as an available number. A <c>null</c> entry is a sample at which the value was not
	/// known.
	/// </summary>
	IReadOnlyList<double?> Values { get; }

	/// <summary>Raised after <see cref="Values" /> changed. May be raised on any thread.</summary>
	event EventHandler? Changed;
}
