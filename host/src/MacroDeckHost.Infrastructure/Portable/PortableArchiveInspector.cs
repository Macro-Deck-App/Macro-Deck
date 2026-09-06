using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Infrastructure.Portable;

public sealed class PortableArchiveInspector : IPortableArchiveInspector
{
	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly IIntegrationConfigStore _configStore;

	public PortableArchiveInspector(IIntegrationRegistry integrationRegistry, IIntegrationConfigStore configStore)
	{
		_integrationRegistry = integrationRegistry;
		_configStore = configStore;
	}

	public async Task<Result<PortableArchiveInfo, PortabilityError>> Inspect(byte[] archiveBytes,
		CancellationToken cancellationToken)
	{
		var manifest = PortableArchive.ReadManifest(archiveBytes);
		if (manifest is null)
		{
			return Result.Fail<PortableArchiveInfo, PortabilityError>(PortabilityError.InvalidArchive);
		}

		if (manifest.FormatVersion > PortableArchiveManifest.CurrentFormatVersion)
		{
			return Result.Fail<PortableArchiveInfo, PortabilityError>(PortabilityError.UnsupportedVersion);
		}

		var integrations = new List<PortableIntegrationInfo>();
		foreach (var required in manifest.Contents.Integrations)
		{
			cancellationToken.ThrowIfCancellationRequested();
			integrations.Add(await Resolve(required));
		}

		var info = new PortableArchiveInfo(manifest.Kind,
			manifest.Contents.Name,
			manifest.AppVersion,
			manifest.CreatedAt,
			manifest.Encryption is not null,
			manifest.IncludesSecrets,
			manifest.Contents.FolderCount,
			manifest.Contents.WidgetCount,
			manifest.Contents.IconCount,
			manifest.Contents.ScriptCount,
			manifest.Contents.SecretCount,
			manifest.Contents.VariableCount,
			integrations);

		return Result.Ok<PortableArchiveInfo, PortabilityError>(info);
	}

	private async Task<PortableIntegrationInfo> Resolve(PortableIntegrationRequirement required)
	{
		var installed = _integrationRegistry.Integrations
			.FirstOrDefault(integration => integration.Id == required.Id);
		if (installed is null)
		{
			return new PortableIntegrationInfo(required.Id,
				required.Name,
				required.Version,
				required.RequiresConfiguration,
				PortableIntegrationAvailability.Missing);
		}

		var needsConfiguration = installed is IConfigFlowProvider;
		var availability = await Availability(installed, needsConfiguration);
		return new PortableIntegrationInfo(installed.Id,
			installed.Name,
			installed.Version,
			needsConfiguration,
			availability);
	}

	private async Task<PortableIntegrationAvailability> Availability(IIntegration integration,
		bool needsConfiguration)
	{
		if (!_integrationRegistry.IsEnabled(integration.Id))
		{
			return PortableIntegrationAvailability.Disabled;
		}

		if (!needsConfiguration)
		{
			return PortableIntegrationAvailability.Ready;
		}

		var entries = await _configStore.List(integration.Id);
		return entries.Count > 0
			? PortableIntegrationAvailability.Ready
			: PortableIntegrationAvailability.NotConfigured;
	}
}
