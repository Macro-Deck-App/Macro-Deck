using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Variables;

public static class VariableEventScope
{
	public static bool IsLocalToRun(VariableEntity variable)
	{
		return variable.Scope is VariableScope.Script or VariableScope.Automation;
	}
}
