namespace MacroDeckHost.Domain.Enums;

public enum VariableError
{
	ValidationError,
	NotFound,
	AlreadyExists,
	InvalidName,
	InvalidValue,
	NotEditable,
	NotOwnedByIntegration,
	InternalError,
	NotWritable,
	OwnerUnavailable,
	WriteFailed
}
