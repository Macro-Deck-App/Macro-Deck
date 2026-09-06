namespace MacroDeckHost.Application.Applications;

public sealed record ResolvedApplication(string Path, string Name, string? Arguments = null);

public interface IApplicationPathResolver
{
	ResolvedApplication? Resolve(string path);
}
