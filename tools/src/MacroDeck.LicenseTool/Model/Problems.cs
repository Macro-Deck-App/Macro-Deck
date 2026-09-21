namespace MacroDeck.LicenseTool.Model;

internal sealed class Problems
{
	private readonly SortedSet<string> _messages = new(StringComparer.Ordinal);

	public IReadOnlyCollection<string> Messages => _messages;

	public bool Any => _messages.Count > 0;

	public void Add(string message) => _messages.Add(message);
}
