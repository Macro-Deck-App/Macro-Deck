using SuchByte.MacroDeck.ExtensionStore;

namespace SuchByte.MacroDeck.Models;

public class ApiV2ExtensionSummary
{
    public string? PackageId { get; set; }
    public ExtensionType? ExtensionType { get; set; }
    public string? Name { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? GitHubRepository { get; set; }
    public string? DSupportUserId { get; set; }
}
