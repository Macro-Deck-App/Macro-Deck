namespace MacroDeckHost.Tests.UnitTests.Discord;

internal sealed class EnvironmentScope : IDisposable
{
	private readonly Dictionary<string, string?> _original = new(StringComparer.Ordinal);

	public string? this[string name]
	{
		set
		{
			_original.TryAdd(name, Environment.GetEnvironmentVariable(name));
			Environment.SetEnvironmentVariable(name, value);
		}
	}

	public void Dispose()
	{
		foreach (var (name, value) in _original)
		{
			Environment.SetEnvironmentVariable(name, value);
		}
	}
}
