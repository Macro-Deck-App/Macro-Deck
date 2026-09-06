namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class GetStoreStatusResponse
{
	public StoreRegistryStatusBody Registry { get; set; } = new();

	/// <summary>Whether the host has developer mode on. The store surfaces it so the UI knows whether an
	/// unsigned install can be offered at all; it is never the permission itself - the host re-reads its
	/// own preference on every install that asks for unsigned consent.</summary>
	public bool DeveloperMode { get; set; }
}
