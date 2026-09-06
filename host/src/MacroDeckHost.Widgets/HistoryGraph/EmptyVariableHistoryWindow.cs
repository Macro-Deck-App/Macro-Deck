using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Widgets.HistoryGraph;

/// <summary>The window a graph gets while it names no variable - permanently empty and never sampled, so
/// an unconfigured card costs nothing rather than being a special case in the session.</summary>
internal sealed class EmptyVariableHistoryWindow : IVariableHistoryWindow
{
	public static readonly EmptyVariableHistoryWindow Instance = new();

	private EmptyVariableHistoryWindow()
	{
	}

	public event EventHandler? Changed
	{
		add { }
		remove { }
	}

	public IReadOnlyList<double> Values => [];

	public void Dispose()
	{
	}
}
