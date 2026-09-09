using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Integrations;

/// <summary>
/// Implemented by an in-process integration that learns of its own state changes and can therefore ask
/// for its eager variables to be read before their cadence comes round.
/// </summary>
public interface IVariableRefreshSignalConsumer
{
	void UseVariableRefreshSignal(IVariableRefreshSignal signal);
}
