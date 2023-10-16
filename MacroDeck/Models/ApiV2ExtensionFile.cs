namespace SuchByte.MacroDeck.Models;

public class ApiV2ExtensionFile
{
    public string? Version { get; set; }
    public int? MinApiVersion { get; set; }
    public string? PackageFileName { get; set; }
    public string? IconFileName { get; set; }
    public string? Readme { get; set; }
    public string? FileHash { get; set; }
    public string? LicenseName { get; set; }
    public string? LicenseUrl { get; set; }
    public DateTime? UploadDateTime { get; set; }
}