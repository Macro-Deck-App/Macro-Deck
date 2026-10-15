using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Variables;

public interface IVariableTemplateRenderer
{
	Task<string> RenderAsync(string templateText, VariableScope contextScope, string? contextScopeRefId);

	Task<VariableContext> CreateContextAsync(VariableScope contextScope, string? contextScopeRefId);

	Task<string> RenderAsync(string templateText, VariableScopeChain chain);

	Task<VariableContext> CreateContextAsync(VariableScopeChain chain);

	string Render(string templateText, VariableContext context);
}
