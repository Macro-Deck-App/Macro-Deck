using System.Text.Json;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Integrations.ConfigFlow;
using MacroDeck.Sdk.Migration;
using MacroDeckHost.Application.Migration;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Security.KeyRing;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Migration;

public sealed class MigrationService : IMigrationService
{
	private const string ImportedPackSuffix = " (Macro Deck 2)";

	private readonly IReadOnlyList<IMigrationSource> _sources;
	private readonly IProfilePortabilityService _profiles;
	private readonly IIconPackService _iconPacks;
	private readonly IIconImportService _iconImport;
	private readonly ISecretService _secrets;
	private readonly IIntegrationConfigStore _configStore;
	private readonly IIntegrationRegistry _integrations;
	private readonly IIntegrationLifecycle _lifecycle;
	private readonly IVariableService _variables;
	private readonly IKeyRingProtectionService _keyRing;
	private readonly ILogger _logger;

	public MigrationService(
		IEnumerable<IMigrationSource> sources,
		IProfilePortabilityService profiles,
		IIconPackService iconPacks,
		IIconImportService iconImport,
		ISecretService secrets,
		IIntegrationConfigStore configStore,
		IIntegrationRegistry integrations,
		IIntegrationLifecycle lifecycle,
		IVariableService variables,
		IKeyRingProtectionService keyRing,
		ILogger logger)
	{
		_sources = sources.ToList();
		_profiles = profiles;
		_iconPacks = iconPacks;
		_iconImport = iconImport;
		_secrets = secrets;
		_configStore = configStore;
		_integrations = integrations;
		_lifecycle = lifecycle;
		_variables = variables;
		_keyRing = keyRing;
		_logger = logger;
	}

	public IReadOnlyList<MigrationSourceDescriptor> GetSources()
		=> _sources
			.Select(source => new MigrationSourceDescriptor(source.Id, source.Name, source.TryDetectDefaultPath()))
			.ToList();

	/// <summary>
	/// Reads the source without applying it. The plan is disposed before it is returned: a preview only
	/// ever reports counts and warnings, so whatever the source had to unpack to produce them is not
	/// needed once it has.
	/// </summary>
	public async Task<Result<MigrationPlan, MigrationError>> Preview(
		MigrationRequest request,
		CancellationToken cancellationToken)
	{
		var read = await ReadPlan(request, cancellationToken);
		read.Data?.Dispose();
		return read;
	}

	private async Task<Result<MigrationPlan, MigrationError>> ReadPlan(
		MigrationRequest request,
		CancellationToken cancellationToken)
	{
		var source = _sources.FirstOrDefault(candidate =>
			string.Equals(candidate.Id, request.SourceId, StringComparison.Ordinal));

		if (source is null)
		{
			return Result.Fail<MigrationPlan, MigrationError>(MigrationError.UnknownSource);
		}

		return await source.Read(request, cancellationToken);
	}

	public async Task<Result<MigrationOutcome, MigrationError>> Apply(
		MigrationRequest request,
		CancellationToken cancellationToken)
	{
		// Secrets are written through Data Protection, which refuses to generate keys while the ring is
		// locked. Refusing here says so plainly instead of failing halfway through a migration.
		if (_keyRing.Status.State == KeyRingProtectionState.Locked)
		{
			return Result.Fail<MigrationOutcome, MigrationError>(MigrationError.KeyRingLocked,
				"The key ring is locked, so credentials cannot be stored");
		}

		var read = await ReadPlan(request, cancellationToken);
		if (!read.Success)
		{
			return Result.Fail<MigrationOutcome, MigrationError>(read.Error!.Value, read.ErrorMessage);
		}

		// Held open for the whole apply: the icons below are files the source unpacked, and disposing the
		// plan is what removes them again.
		using var plan = read.Data!;

		try
		{
			var iconIds = await ImportIcons(plan, cancellationToken);
			var profileIds = await ImportProfiles(plan, iconIds, cancellationToken);
			await ImportVariables(plan);
			await ImportConfigurations(plan, cancellationToken);

			return Result.Ok<MigrationOutcome, MigrationError>(new MigrationOutcome(plan, profileIds));
		}
		catch (Exception ex) when (ex is not OperationCanceledException)
		{
			_logger.Error(ex, "Failed to apply a {SourceId} migration", request.SourceId);
			return Result.Fail<MigrationOutcome, MigrationError>(MigrationError.StorageFailure);
		}
	}

	/// <summary>
	/// Pulls each referenced image through the normal icon import, which converts it and deduplicates it
	/// against what the catalogue already holds. Returns the map from the plan's placeholder ids to the
	/// icons that now exist.
	/// </summary>
	private async Task<Dictionary<Guid, Guid>> ImportIcons(MigrationPlan plan, CancellationToken cancellationToken)
	{
		var iconIds = new Dictionary<Guid, Guid>();
		if (plan.Icons.Count == 0)
		{
			return iconIds;
		}

		var packIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

		foreach (var request in plan.Icons)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (!packIds.TryGetValue(request.PackName, out var packId))
			{
				var pack = await _iconPacks.Create(request.PackName + ImportedPackSuffix,
					description: null,
					author: null,
					version: null);

				if (!pack.Success)
				{
					plan.Warnings.Add(new MigrationWarning(MigrationWarningKind.MissingIcon,
						request.PackName,
						AppStrings.Migration.Warning.IconPackNotCreated()));
					continue;
				}

				packId = pack.Data!.Id;
				packIds[request.PackName] = packId;
			}

			await using var stream = File.OpenRead(request.FilePath);
			var imported = await _iconImport.ImportSingle(packId,
				new IconImportFile(Path.GetFileName(request.FilePath), stream),
				cancellationToken);

			if (imported.Success)
			{
				iconIds[request.PlaceholderId] = imported.Data!.Icon.Id;
			}
			else
			{
				plan.Warnings.Add(new MigrationWarning(MigrationWarningKind.MissingIcon,
					request.Name,
					AppStrings.Migration.Warning.IconImportFailed()));
			}
		}

		return iconIds;
	}

	/// <summary>
	/// Hands each profile to the existing importer, which mints its ids, rewrites the references inside
	/// widget data and persists it. The placeholder icon ids are rewritten first, with the same remapper,
	/// because the importer only ever rewrites ids it minted itself.
	/// </summary>
	private async Task<List<Guid>> ImportProfiles(
		MigrationPlan plan,
		Dictionary<Guid, Guid> iconIds,
		CancellationToken cancellationToken)
	{
		var created = new List<Guid>();

		foreach (var content in plan.Profiles)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (iconIds.Count > 0 && content.Profile is not null)
			{
				foreach (var folder in content.Profile.Folders)
				{
					foreach (var widget in folder.Widgets)
					{
						widget.Data = PortableGuidRemapper.Remap(widget.Data, iconIds);
					}
				}
			}

			var imported = await _profiles.ImportContent(content, [], cancellationToken);
			if (imported.Success)
			{
				created.Add(imported.Data!.Id);
			}
			else
			{
				plan.Warnings.Add(new MigrationWarning(MigrationWarningKind.SkippedWidget,
					content.Profile?.Name ?? string.Empty,
					AppStrings.Migration.Warning.ProfileNotCreated()));
			}
		}

		return created;
	}

	/// <summary>
	/// The SDK names only the two kinds a migration can produce; the store's own set is wider and not a
	/// plugin-facing contract.
	/// </summary>
	private static SecretKind ToSecretKind(MigratedSecretKind kind)
		=> kind == MigratedSecretKind.Password ? SecretKind.Password : SecretKind.Secret;

	private async Task ImportVariables(MigrationPlan plan)
	{
		foreach (var variable in plan.Variables)
		{
			var created = await _variables.CreateUserVariable(variable.Name,
				VariableScope.Global,
				scopeRefId: null,
				variable.Type,
				variable.Value,
				variable.DecimalPlaces);

			if (created.Success)
			{
				continue;
			}

			// A name that is already taken is the normal outcome of migrating twice, and is reported
			// rather than overwriting whatever the local variable currently holds.
			plan.Warnings.Add(new MigrationWarning(MigrationWarningKind.SkippedVariable,
				variable.Name,
				created.Error == VariableError.AlreadyExists
					? AppStrings.Migration.Warning.VariableAlreadyExists()
					: AppStrings.Migration.Warning.VariableNotCreated()));
		}
	}

	/// <summary>
	/// Creates the configuration entries, then enables and reinitializes each integration - a stored entry
	/// on its own leaves the integration switched off, which is not what "migrated" means to anyone.
	/// </summary>
	private async Task ImportConfigurations(MigrationPlan plan, CancellationToken cancellationToken)
	{
		foreach (var config in plan.IntegrationConfigs)
		{
			var existing = await _configStore.List(config.IntegrationId);
			if (existing.Count > 0)
			{
				plan.Warnings.Add(new MigrationWarning(MigrationWarningKind.AlreadyConfigured,
					config.IntegrationId,
					AppStrings.Migration.Warning.IntegrationAlreadyConfigured()));
				continue;
			}

			var values = new Dictionary<string, JsonElement>(config.Values, StringComparer.Ordinal);
			foreach (var (name, secret) in config.Secrets)
			{
				var secretId = await _secrets.Create(secret.Value, ToSecretKind(secret.Kind));
				values[name] = JsonSerializer.SerializeToElement(new Dictionary<string, string>
					{ [SecretReferenceJson.PropertyName] = secretId.ToString() });
			}

			await _configStore.Create(config.IntegrationId, config.Title, values);

			_integrations.SetEnabled(config.IntegrationId, true);
			await _lifecycle.ReinitializeAsync(config.IntegrationId, cancellationToken);
		}
	}
}
