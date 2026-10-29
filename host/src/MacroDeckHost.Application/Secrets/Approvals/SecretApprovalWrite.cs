namespace MacroDeckHost.Application.Secrets.Approvals;

public readonly record struct SecretApprovalWrite(
	SecretApprovalMode Mode,
	Func<string, string>? Rewriter = null,
	IReadOnlyCollection<string>? Restored = null)
{
	public static SecretApprovalWrite Preserve => new(SecretApprovalMode.Preserve);

	public static SecretApprovalWrite Approve => new(SecretApprovalMode.Approve);

	public static SecretApprovalWrite Withhold => new(SecretApprovalMode.Withhold);

	public static SecretApprovalWrite Restore(IReadOnlyCollection<string> approvedSnapshots)
	{
		return new SecretApprovalWrite(SecretApprovalMode.Restore, null, approvedSnapshots);
	}

	public static SecretApprovalWrite Rewrite(Func<string, string> rewriter)
	{
		return new SecretApprovalWrite(SecretApprovalMode.Rewrite, rewriter);
	}
}
