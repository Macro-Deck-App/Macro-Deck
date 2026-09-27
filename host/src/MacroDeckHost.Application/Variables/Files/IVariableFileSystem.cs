namespace MacroDeckHost.Application.Variables.Files;

public interface IVariableFileSystem
{
	Task<FileReadOutcome> ReadAsync(string path, CancellationToken cancellationToken = default);

	Task WriteAsync(string path, string content, CancellationToken cancellationToken = default);

	IDisposable Watch(string path, Action changed);
}

public sealed record FileReadOutcome(string? Content)
{
	public static readonly FileReadOutcome Unavailable = new((string?)null);

	public bool Available => Content is not null;
}
