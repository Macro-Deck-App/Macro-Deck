namespace MacroDeckHost.Application.ClientTargets;

/// <summary>
/// Prepares a physical device to run one Web Client target (issue #727). Everything device-specific -
/// which tool flashes it, what has to be written where, which commands restart it - belongs to the
/// implementation; the host only sequences the steps and renders them.
/// </summary>
public interface IWebClientTargetProvisioner
{
	/// <summary>The target id this provisions, matching the build's <c>WebClientTarget.id</c>.</summary>
	string TargetId { get; }

	/// <summary>
	/// Whether provisioning can run at all on this installation - the transport it needs is present
	/// and enabled. A provisioner that answers false is not offered rather than failing on step one.
	/// </summary>
	Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

	Task<WebClientTargetProvisioningResult> StartAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Advances past <paramref name="stepId"/>. An unknown step id restarts the walkthrough rather
	/// than throwing: a stale browser tab must not be able to drive the provisioner into an
	/// undefined state.
	/// </summary>
	Task<WebClientTargetProvisioningResult> AdvanceAsync(
		string stepId,
		IReadOnlyDictionary<string, string> input,
		CancellationToken cancellationToken);
}
