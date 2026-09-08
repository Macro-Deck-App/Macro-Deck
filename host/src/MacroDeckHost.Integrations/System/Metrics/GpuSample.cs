namespace MacroDeckHost.Integrations.System.Metrics;

internal sealed record GpuSample(string? Name, double? UsagePercent);
