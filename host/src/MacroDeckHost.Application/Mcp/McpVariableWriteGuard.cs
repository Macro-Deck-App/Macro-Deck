namespace MacroDeckHost.Application.Mcp;

public interface IMcpVariableWriteGuard
{
	bool Refuses(string writerIntegrationId, string variableName);
}

public sealed class McpVariableWriteGuard : IMcpVariableWriteGuard
{
	public const string RefusalCode = "VARIABLE_APPROVAL_REQUIRED";

	public const string RefusalMessage
		= "VARIABLE_APPROVAL_REQUIRED: an approved secret block reads this variable; the MCP may not change it.";

	private readonly IMcpExecutionRegistry _registry;
	private readonly IMcpProtectedReferences _references;

	public McpVariableWriteGuard(IMcpExecutionRegistry registry, IMcpProtectedReferences references)
	{
		_registry = registry;
		_references = references;
	}

	public bool Refuses(string writerIntegrationId, string variableName)
	{
		return _registry.IsActive(writerIntegrationId)
			&& !_registry.HasConsent(writerIntegrationId, variableName)
			&& _references.ProtectedVariables().Contains(variableName);
	}
}
