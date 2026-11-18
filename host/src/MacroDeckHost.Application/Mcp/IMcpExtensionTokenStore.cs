namespace MacroDeckHost.Application.Mcp;

public interface IMcpExtensionTokenStore
{
	Task<bool> Store(Guid tokenId, string token);

	Task Remove(Guid tokenId);
}
