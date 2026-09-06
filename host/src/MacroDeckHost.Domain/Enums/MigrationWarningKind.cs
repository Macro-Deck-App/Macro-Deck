namespace MacroDeckHost.Domain.Enums;

public enum MigrationWarningKind
{
	UnsupportedAction,
	UnsupportedTrigger,
	UnsupportedFeature,
	MissingIcon,
	SkippedCredentials,
	SkippedVariable,
	SkippedWidget,
	DuplicateSourceFile,
	AlreadyConfigured
}
