namespace MacroDeckHost.Domain.Enums;

public enum ExecutionOrigin
{
	// Defaults to Client so a future call site that forgets to set this fails closed - it gets
	// gated while the host is locked instead of silently bypassing the check.
	Client = 0,
	Host = 1
}
