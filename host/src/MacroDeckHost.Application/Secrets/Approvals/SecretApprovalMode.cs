namespace MacroDeckHost.Application.Secrets.Approvals;

public enum SecretApprovalMode
{
	Preserve = 0,
	Approve,
	Rewrite,
	Withhold,
	Restore
}
