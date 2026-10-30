namespace MacroDeckHost.Application.Secrets;

public sealed class SecretScrubDeferral
{
	private readonly HashSet<Guid> _deferred = [];

	public bool Active { get; private set; }

	public IReadOnlyCollection<Guid> Deferred => _deferred;

	public void Activate()
	{
		Active = true;
	}

	public void Defer(IEnumerable<Guid> secretIds)
	{
		_deferred.UnionWith(secretIds);
	}

	public IReadOnlyList<Guid> Take()
	{
		var taken = _deferred.ToList();
		_deferred.Clear();
		return taken;
	}
}
