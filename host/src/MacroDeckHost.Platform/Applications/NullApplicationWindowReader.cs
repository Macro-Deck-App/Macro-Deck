namespace MacroDeckHost.Platform.Applications;

internal sealed class NullApplicationWindowReader : IApplicationWindowReader
{
	public ApplicationWindowAvailability Availability => ApplicationWindowAvailability.Unsupported;

	public IReadOnlyDictionary<int, IReadOnlyList<ApplicationWindowState>> Read(IReadOnlySet<int> processIds)
	{
		return new Dictionary<int, IReadOnlyList<ApplicationWindowState>>();
	}

	public void Dispose()
	{
	}
}
