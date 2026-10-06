namespace MacroDeckHost.Application.Icons.Included;

public sealed record IncludedIconAsset(string Name, byte[] Content);

public interface IIncludedIconAssets
{
	IReadOnlyList<IncludedIconAsset> Load();
}
