namespace MacroDeckHost.Application.Mcp;

public static class McpExecutionScope
{
	private static readonly AsyncLocal<Guid?> _current = new();

	public static Guid? Current => _current.Value;

	public static IDisposable Enter(Guid? tokenId)
	{
		var previous = _current.Value;
		_current.Value = tokenId ?? previous;
		return new Restore(previous);
	}

	private sealed class Restore : IDisposable
	{
		private readonly Guid? _previous;

		public Restore(Guid? previous)
		{
			_previous = previous;
		}

		public void Dispose()
		{
			_current.Value = _previous;
		}
	}
}
