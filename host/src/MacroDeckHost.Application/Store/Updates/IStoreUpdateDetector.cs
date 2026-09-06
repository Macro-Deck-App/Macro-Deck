namespace MacroDeckHost.Application.Store.Updates;

/// <summary>Compares installed extension versions against the registry's latest release. Notify-only: it
/// never starts an install, it only reports what could be installed.</summary>
public interface IStoreUpdateDetector
{
	IReadOnlyList<StoreAvailableUpdate> Check();
}
