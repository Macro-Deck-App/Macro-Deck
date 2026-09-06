namespace MacroDeckHost.Domain.Enums;

public enum IconImportBatchState
{
	Discovering,
	Processing,
	Completed,
	CompletedWithErrors,
	Failed,
	Cancelled
}
