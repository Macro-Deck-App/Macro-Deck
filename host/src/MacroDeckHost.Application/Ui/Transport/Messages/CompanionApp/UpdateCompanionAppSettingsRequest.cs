namespace MacroDeckHost.Application.Ui.Transport.Messages.CompanionApp;

public sealed class UpdateCompanionAppSettingsRequest
{
	public bool AutoUpdate { get; init; }
}

public sealed class UpdateCompanionAppPluginAccessRequest
{
	public bool AllowPlugins { get; init; }
}
