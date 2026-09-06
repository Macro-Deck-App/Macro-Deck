namespace MacroDeckHost.Application.Variables;

/// <summary>
/// A rolling window of a numeric variable's recent values, sampled on a fixed cadence so a graph of it
/// advances at an even pace whether the value changes or not.
/// </summary>
public interface IVariableHistory
{
	/// <summary>
	/// Opens a window on <paramref name="variableName" />, keeping at most <paramref name="capacity" />
	/// values. Sampling for a variable runs only while at least one window on it is open; disposing the
	/// last one stops it.
	/// </summary>
	IVariableHistoryWindow Open(string variableName, int capacity);
}

/// <summary>One holder's view of a variable's rolling window. Disposing it releases the hold.</summary>
public interface IVariableHistoryWindow : IDisposable
{
	/// <summary>The retained values, oldest first. Empty until the variable first reads as an available
	/// number.</summary>
	IReadOnlyList<double> Values { get; }

	/// <summary>Raised after a sample changed <see cref="Values" />.</summary>
	event EventHandler? Changed;
}
