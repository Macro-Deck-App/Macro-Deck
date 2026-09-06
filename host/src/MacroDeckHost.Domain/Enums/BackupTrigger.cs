namespace MacroDeckHost.Domain.Enums;

public enum BackupTrigger
{
	Manual,
	Scheduled,
	BeforeHostUpdate,
	BeforePluginUpdate,
	BeforeRestore,
	Imported
}
