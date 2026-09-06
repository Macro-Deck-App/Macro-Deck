namespace MacroDeck.Sdk;

/// <summary>
/// Marks a developer/sample integration that ships for demonstration only. The host skips loading
/// these unless developer integrations are explicitly enabled, so the core never needs to know any
/// specific sample integration by id.
/// </summary>
public interface ISampleIntegration;
