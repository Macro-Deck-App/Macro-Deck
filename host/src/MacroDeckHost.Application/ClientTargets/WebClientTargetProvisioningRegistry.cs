namespace MacroDeckHost.Application.ClientTargets;

/// <summary>
/// Resolves the provisioner for a target id (issue #727). Registered provisioners are injected, so a
/// new target's setup workflow is one DI registration and nothing here changes.
/// </summary>
public sealed class WebClientTargetProvisioningRegistry
{
	private readonly IReadOnlyDictionary<string, IWebClientTargetProvisioner> _provisioners;

	public WebClientTargetProvisioningRegistry(IEnumerable<IWebClientTargetProvisioner> provisioners)
	{
		_provisioners = provisioners
			.GroupBy(provisioner => provisioner.TargetId, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
	}

	public IReadOnlyCollection<string> TargetIds => (IReadOnlyCollection<string>)_provisioners.Keys;

	public IWebClientTargetProvisioner? Resolve(string targetId)
		=> _provisioners.TryGetValue(targetId, out var provisioner) ? provisioner : null;
}
