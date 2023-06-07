﻿using System.Collections.Generic;
using System.Text.Json.Serialization;
using SuchByte.MacroDeck.ExtensionStore;

namespace SuchByte.MacroDeck.Models;

public class ApiV2Extension
{
    public string? PackageId { get; set; }
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ExtensionType? ExtensionType { get; set; }
    public string? Name { get; set; }
    public string? Category { get; set; }
    public string? Author { get; set; }
    public string? Description { get; set; }
    public string? GitHubRepository { get; set; }
    public string? DSupportUserId { get; set; }
    public long TotalDownloads { get; set; }
    public IList<ApiV2ExtensionFile> ExtensionFiles { get; set; }
}