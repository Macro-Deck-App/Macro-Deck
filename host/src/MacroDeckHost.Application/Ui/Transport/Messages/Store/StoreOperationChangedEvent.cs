namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class StoreOperationChangedEvent
{
	public StoreOperationBody Operation { get; set; } = new();
}
