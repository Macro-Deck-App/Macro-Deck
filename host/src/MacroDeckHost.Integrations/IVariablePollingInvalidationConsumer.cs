using MacroDeckHost.Application.Variables;

namespace MacroDeckHost.Integrations;

public interface IVariablePollingInvalidationConsumer
{
	void UseVariablePollingInvalidation(IVariablePollingInvalidationSignal signal);
}
