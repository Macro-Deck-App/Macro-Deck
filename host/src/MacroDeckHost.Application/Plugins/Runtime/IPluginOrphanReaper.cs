namespace MacroDeckHost.Application.Plugins.Runtime;

public interface IPluginOrphanReaper
{
	Task ReapAsync(CancellationToken cancellationToken = default);
}
