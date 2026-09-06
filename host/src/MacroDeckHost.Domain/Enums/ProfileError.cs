namespace MacroDeckHost.Domain.Enums;

public enum ProfileError
{
	ValidationError,
	NotFound,
	CannotDeleteLastProfile,
	IsVirtual,
	GridTooSmall,
	GridLockedByDevice,
	InternalError
}
