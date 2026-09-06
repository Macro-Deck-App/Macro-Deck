namespace MacroDeckHost.Application.Plugins.Trust;

public enum PluginTrustGateAction
{
	None,
	Backfill,
	Upgrade
}

public sealed record PluginTrustGateDecision(bool Permitted, PluginTrustGateAction Action, string? RefusalReason)
{
	public static PluginTrustGateDecision Permit(PluginTrustGateAction action = PluginTrustGateAction.None)
		=> new(true, action, null);

	public static PluginTrustGateDecision Refuse(string reason) => new(false, PluginTrustGateAction.None, reason);
}

/// <summary>Decides whether an installed plugin version may launch, given the trust tier it was admitted
/// at and what it verifies as right now. Shared by <c>PluginInstaller</c> (activation) and
/// <c>PluginSupervisor</c> (every launch attempt) so the two never drift on what "still trusted" means.
/// </summary>
public static class PluginTrustGate
{
	public static PluginTrustGateDecision Evaluate(PluginTrustVerdict? admittedVerdict,
		string? admittedCertificateId,
		PluginTrustResult onDisk,
		bool baselineExists)
	{
		ArgumentNullException.ThrowIfNull(onDisk);

		if (admittedVerdict is null)
		{
			if (baselineExists)
			{
				return PluginTrustGateDecision.Refuse(
					"No trust record exists for this installed plugin, and the baseline has already been " +
					"established; a missing record is anomalous, not grandfathered.");
			}

			return onDisk.Verdict is PluginTrustVerdict.Trusted or PluginTrustVerdict.Unsigned
				? PluginTrustGateDecision.Permit(PluginTrustGateAction.Backfill)
				: PluginTrustGateDecision.Refuse(
					$"The installed plugin has no trust record and does not verify as trusted or unsigned " +
					$"({onDisk.Verdict}).");
		}

		if (admittedVerdict == PluginTrustVerdict.Trusted)
		{
			if (onDisk.Verdict != PluginTrustVerdict.Trusted)
			{
				return PluginTrustGateDecision.Refuse(
					$"The installed plugin was admitted as trusted but now verifies as {onDisk.Verdict}.");
			}

			return string.Equals(onDisk.CertificateId, admittedCertificateId, StringComparison.Ordinal)
				? PluginTrustGateDecision.Permit()
				: PluginTrustGateDecision.Refuse(
					"The installed plugin still verifies as trusted, but its certificate id no longer " +
					"matches the one it was admitted with.");
		}

		return onDisk.Verdict switch
		{
			PluginTrustVerdict.Unsigned => PluginTrustGateDecision.Permit(),
			PluginTrustVerdict.Trusted => PluginTrustGateDecision.Permit(PluginTrustGateAction.Upgrade),
			_ => PluginTrustGateDecision.Refuse(
				$"The installed plugin was admitted as unsigned and now verifies as {onDisk.Verdict}.")
		};
	}
}
