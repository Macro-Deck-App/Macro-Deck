using MacroDeckHost.Application.ClientTargets;

namespace MacroDeckHost.Tests.UnitTests.ClientTargets;

internal sealed class FakeScratchWriter : IFileSystemScratchWriter
{
	public List<string> WrittenContents { get; } = [];

	public List<string> DeletedPaths { get; } = [];

	public string PathToReturn { get; set; } = "/tmp/macro-deck-scratch";

	public Task<string> WriteAsync(string content, CancellationToken cancellationToken)
	{
		WrittenContents.Add(content);
		return Task.FromResult(PathToReturn);
	}

	public void Delete(string path) => DeletedPaths.Add(path);
}
