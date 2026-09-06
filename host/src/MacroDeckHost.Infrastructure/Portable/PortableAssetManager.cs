using System.Globalization;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Scripts;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using Mediator;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Portable;

public sealed class PortableAssetManager : IPortableAssetManager
{
	private const string ImportedIconsPackSourceId = "portable:imported-icons";
	private const string ImportedIconsPackName = "Imported Icons";

	private readonly IIconPackCache _iconPackCache;
	private readonly IIconStorage _iconStorage;
	private readonly ISecretService _secretService;
	private readonly IScriptService _scriptService;
	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly IWidgetVariableCloner _variableCloner;
	private readonly IMediator _mediator;
	private readonly IAppPreferenceService _preferences;
	private readonly ILocalizationResolver _localization;
	private readonly ILogger _logger;

	public PortableAssetManager(
		IIconPackCache iconPackCache,
		IIconStorage iconStorage,
		ISecretService secretService,
		IScriptService scriptService,
		IIntegrationRegistry integrationRegistry,
		IWidgetVariableCloner variableCloner,
		IMediator mediator,
		IAppPreferenceService preferences,
		ILocalizationResolver localization,
		ILogger logger)
	{
		_iconPackCache = iconPackCache;
		_iconStorage = iconStorage;
		_secretService = secretService;
		_scriptService = scriptService;
		_integrationRegistry = integrationRegistry;
		_variableCloner = variableCloner;
		_mediator = mediator;
		_preferences = preferences;
		_localization = localization;
		_logger = logger;
	}

	public async Task<PortableAssetBundle> Collect(IReadOnlyList<PortableWidgetSource> widgets,
		PortableExportOptions options,
		CancellationToken cancellationToken)
	{
		var widgetData = widgets.Select(widget => widget.Data).ToList();
		var scripts = CollectScripts(widgetData);

		var referencingData = widgetData.Concat(scripts.Select(script => (string?)script.Flows)).ToList();

		var icons = new List<PortableIcon>();
		var files = new List<PortableIconFile>();
		var referencedIcons = options.IncludeIcons ? ReferencedIconIds(referencingData) : [];
		foreach (var iconId in referencedIcons)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var icon = _iconPackCache.GetIconById(iconId);
			if (icon is null || icon.ProcessingState != IconProcessingState.Ready)
			{
				continue;
			}

			var master = ReadVariant(icon, IconVariants.Master);
			if (master is null)
			{
				_logger.Warning("Skipping icon {IconId} during export: master variant missing", iconId);
				continue;
			}

			var collectedSizes = new List<int>();
			var fileHashes = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[IconVariants.Master] = ContentHash.Compute(master)
			};
			files.Add(new PortableIconFile(icon.Id, IconVariants.Master, master));
			foreach (var size in icon.AvailableSizes)
			{
				var name = size.ToString(CultureInfo.InvariantCulture);
				var bytes = ReadVariant(icon, name);
				if (bytes is null)
				{
					continue;
				}

				files.Add(new PortableIconFile(icon.Id, name, bytes));
				fileHashes[name] = ContentHash.Compute(bytes);
				collectedSizes.Add(size);
			}

			icons.Add(new PortableIcon
			{
				Id = icon.Id,
				Name = icon.Name,
				Width = icon.Width,
				Height = icon.Height,
				IsAnimated = icon.IsAnimated,
				FrameCount = icon.FrameCount,
				SourceContentHash = icon.SourceContentHash ?? icon.DeclaredSourceContentHash,
				FileContentHashes = fileHashes,
				OriginalFileName = icon.OriginalFileName,
				OriginalFormat = icon.OriginalFormat,
				AvailableSizes = collectedSizes
			});
		}

		var secrets = options.IncludeSecrets ? await CollectSecrets(referencingData) : [];
		var variables = await CollectVariables(widgets);
		var culture = (await _preferences.GetLocalization()).Culture;
		return new PortableAssetBundle(icons,
			files,
			scripts,
			secrets,
			CollectIntegrations(referencingData, culture),
			variables);
	}

	private async Task<List<PortableVariable>> CollectVariables(IReadOnlyList<PortableWidgetSource> widgets)
	{
		var variables = new List<PortableVariable>();
		foreach (var widget in widgets)
		{
			foreach (var snapshot in await _variableCloner.Snapshot(widget.Id))
			{
				variables.Add(new PortableVariable
				{
					WidgetId = widget.Id,
					Name = snapshot.Name,
					Type = snapshot.Type,
					Value = snapshot.Value,
					DecimalPlaces = snapshot.DecimalPlaces
				});
			}
		}

		return variables;
	}

	// A portable bundle is a stored artefact, so the name it records has to be finished text and never a
	// localization reference.
	private List<PortableIntegrationRequirement> CollectIntegrations(
		IReadOnlyList<string?> referencingData,
		string? culture)
	{
		var candidates = _integrationRegistry.Integrations
			.Where(integration => integration is not ISystemIntegration)
			.ToDictionary(integration => integration.Id, StringComparer.Ordinal);

		return IntegrationReferences.Extract(referencingData, candidates.Keys.ToList())
			.Select(id => candidates[id])
			.Select(integration => new PortableIntegrationRequirement
			{
				Id = integration.Id,
				Name = _localization.Resolve(integration.Name, culture) ?? integration.Id,
				Version = integration.Version,
				RequiresConfiguration = integration is IConfigFlowProvider
			})
			.ToList();
	}

	public async Task<IReadOnlyDictionary<Guid, Guid>> Import(PortableContent content,
		IReadOnlyList<PortableIconFile> iconFiles,
		CancellationToken cancellationToken)
	{
		var idMap = new Dictionary<Guid, Guid>();
		await ImportIcons(content, iconFiles, idMap, cancellationToken);

		foreach (var secret in content.Secrets)
		{
			var newId = await _secretService.Create(secret.Value, secret.Kind);
			idMap[secret.Id] = newId;
		}

		await ImportScripts(content, idMap, cancellationToken);

		return idMap;
	}

	public async Task RestoreWidgetVariables(PortableContent content,
		IReadOnlyDictionary<Guid, Guid> widgetIdMap,
		CancellationToken cancellationToken)
	{
		if (content.Variables.Count == 0)
		{
			return;
		}

		var byTargetWidget = new Dictionary<Guid, List<WidgetVariableSnapshot>>();
		foreach (var variable in content.Variables)
		{
			// A hand-edited archive could name a WidgetId that never appeared among the archived widgets,
			// or one that happens to match a live local widget. Falling back to the raw WidgetId as the
			// target would let such an archive attach variables to an arbitrary existing local widget, so
			// entries whose source id is not in the map are skipped rather than trusted.
			if (!widgetIdMap.TryGetValue(variable.WidgetId, out var targetWidgetId))
			{
				continue;
			}

			if (!byTargetWidget.TryGetValue(targetWidgetId, out var snapshots))
			{
				snapshots = [];
				byTargetWidget[targetWidgetId] = snapshots;
			}

			snapshots.Add(new WidgetVariableSnapshot(variable.Name,
				variable.Type,
				variable.Value,
				variable.DecimalPlaces));
		}

		foreach (var (targetWidgetId, snapshots) in byTargetWidget)
		{
			cancellationToken.ThrowIfCancellationRequested();
			await _variableCloner.Restore(targetWidgetId, snapshots);
		}
	}

	private async Task ImportScripts(
		PortableContent content,
		Dictionary<Guid, Guid> idMap,
		CancellationToken cancellationToken)
	{
		if (content.Scripts.Count == 0)
		{
			return;
		}

		foreach (var script in content.Scripts)
		{
			idMap[script.Id] = Guid.NewGuid();
		}

		foreach (var script in content.Scripts)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var result = await _scriptService.Create(script.Name,
				script.Description,
				PortableGuidRemapper.Remap(script.Flows, idMap),
				idMap[script.Id],
				script.Inputs,
				script.RunsOnWidget);

			if (!result.Success)
			{
				_logger.Warning("Skipping script '{ScriptName}' during import: {Error}",
					script.Name,
					result.ErrorMessage);
			}
		}
	}

	private List<PortableScript> CollectScripts(IReadOnlyList<string?> widgetData)
	{
		var stored = _scriptService.GetAll().ToDictionary(script => script.Id);
		var knownIds = stored.Keys.ToHashSet();
		if (knownIds.Count == 0)
		{
			return [];
		}

		var direct = widgetData.SelectMany(data => ScriptReferences.Extract(data, knownIds));
		var resolved = ScriptReferences.ExpandTransitively(direct,
			knownIds,
			id => stored.TryGetValue(id, out var script) ? script.Flows : null);

		return resolved
			.Select(id => stored[id])
			.Select(script => new PortableScript
			{
				Id = script.Id,
				Name = script.Name,
				Description = script.Description,
				Flows = script.Flows,
				Inputs = [.. script.Inputs],
				RunsOnWidget = script.RunsOnWidget
			})
			.ToList();
	}

	private async Task ImportIcons(PortableContent content,
		IReadOnlyList<PortableIconFile> iconFiles,
		Dictionary<Guid, Guid> idMap,
		CancellationToken cancellationToken)
	{
		var filesByIcon = iconFiles
			.GroupBy(file => file.IconId)
			.ToDictionary(group => group.Key,
				group => group.ToList());

		var importable = content.Icons
			.Where(icon => filesByIcon.TryGetValue(icon.Id, out var variants) &&
				variants.Any(variant => variant.Variant == IconVariants.Master))
			.ToList();
		if (importable.Count == 0)
		{
			return;
		}

		var pack = GetOrCreatePack(out var isNewPack);
		var packCreated = false;

		var importedThisRun = new Dictionary<string, Guid>(StringComparer.Ordinal);
		var newIcons = new List<IconEntity>();
		var reused = 0;
		foreach (var portableIcon in importable)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var variants = filesByIcon[portableIcon.Id];
			if (!VerifyBundledFiles(portableIcon, variants))
			{
				continue;
			}

			// The identity is computed from the bundled bytes, never taken from the archive. That is what
			// makes reuse safe across the whole catalog: a hit proves the local icon's master is
			// byte-identical to what this archive carries, so nothing the archive claimed has to be
			// trusted - and a source hash out of an archive describes bytes that are not in it at all.
			var masterBytes = variants.First(variant => variant.Variant == IconVariants.Master).Bytes;
			var masterHash = MasterContentHash.Compute(masterBytes);

			if (importedThisRun.TryGetValue(masterHash.Value, out var justImported))
			{
				idMap[portableIcon.Id] = justImported;
				reused++;
				continue;
			}

			var existing = _iconPackCache.FindByMasterContentHash(masterHash) ??
				await FindByBackfilledMasterHash(masterHash, cancellationToken);
			if (existing is not null)
			{
				idMap[portableIcon.Id] = existing.Id;
				importedThisRun[masterHash.Value] = existing.Id;
				reused++;
				continue;
			}

			if (isNewPack && !packCreated)
			{
				await _iconPackCache.AddOrUpdatePack(pack);
				packCreated = true;
			}

			var newId = Guid.CreateVersion7();
			foreach (var variant in variants)
			{
				await _iconStorage.WriteVariant(pack.Id, newId, variant.Variant, variant.Bytes, cancellationToken);
			}

			newIcons.Add(new IconEntity
			{
				Id = newId,
				PackId = pack.Id,
				Name = portableIcon.Name,
				Width = portableIcon.Width,
				Height = portableIcon.Height,
				IsAnimated = portableIcon.IsAnimated,
				FrameCount = portableIcon.FrameCount,
				MasterContentHash = masterHash.Value,
				DeclaredSourceContentHash =
					ContentHash.Normalize(portableIcon.SourceContentHash ?? portableIcon.Checksum),
				OriginalFileName = portableIcon.OriginalFileName,
				OriginalFormat = portableIcon.OriginalFormat,
				ProcessingState = IconProcessingState.Ready,
				AvailableSizes = portableIcon.AvailableSizes.Order().ToList(),
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			});

			idMap[portableIcon.Id] = newId;
			importedThisRun[masterHash.Value] = newId;
		}

		_logger.Information("Imported {Created} icon(s) and reused {Reused} already present", newIcons.Count, reused);
		if (newIcons.Count == 0)
		{
			return;
		}

		await _iconPackCache.AddIcons(pack.Id, newIcons);
		if (isNewPack)
		{
			await _mediator.Publish(new IconPackCreatedNotification(pack, newIcons.Count), cancellationToken);
		}
		else
		{
			await _mediator.Publish(new IconsAddedNotification(BatchId: null, pack.Id, newIcons), cancellationToken);
		}
	}

	private bool VerifyBundledFiles(PortableIcon icon, List<PortableIconFile> variants)
	{
		foreach (var variant in variants)
		{
			var declared = ContentHash.Normalize(icon.FileContentHashes.GetValueOrDefault(variant.Variant));
			if (declared is null || declared == ContentHash.Compute(variant.Bytes))
			{
				continue;
			}

			_logger.Warning("Skipping icon {IconId} from archive: bundled {Variant} fails its declared hash",
				icon.Id,
				variant.Variant);
			return false;
		}

		return true;
	}

	private async Task<IconEntity?> FindByBackfilledMasterHash(MasterContentHash hash,
		CancellationToken cancellationToken)
	{
		foreach (var candidate in _iconPackCache.GetIconsMissingMasterContentHash())
		{
			cancellationToken.ThrowIfCancellationRequested();
			var bytes = ReadVariant(candidate, IconVariants.Master);
			if (bytes is null)
			{
				continue;
			}

			candidate.MasterContentHash = MasterContentHash.Compute(bytes).Value;
			await _iconPackCache.UpdateIcon(candidate);
			if (candidate.MasterContentHash == hash.Value)
			{
				return candidate;
			}
		}

		return null;
	}

	private IconPackEntity GetOrCreatePack(out bool isNew)
	{
		var existing = _iconPackCache.GetAllPacks()
			.FirstOrDefault(pack => pack.SourceId == ImportedIconsPackSourceId);
		if (existing is not null)
		{
			isNew = false;
			return existing;
		}

		isNew = true;
		return new IconPackEntity
		{
			Id = Guid.CreateVersion7(),
			Name = ImportedIconsPackName,
			Description = "Icons brought in by profile and widget imports.",
			SourceType = IconPackSourceType.MacroDeckImport,
			SourceId = ImportedIconsPackSourceId,
			CreatedAt = DateTime.UtcNow,
			UpdatedAt = DateTime.UtcNow
		};
	}

	private async Task<List<PortableSecret>> CollectSecrets(IReadOnlyList<string?> widgetData)
	{
		var secretIds = new HashSet<Guid>();
		foreach (var data in widgetData)
		{
			foreach (var id in SecretReferences.Extract(data))
			{
				secretIds.Add(id);
			}
		}

		var secrets = new List<PortableSecret>();
		foreach (var id in secretIds)
		{
			var material = await _secretService.ExportForArchive(id);
			if (material is not null)
			{
				secrets.Add(new PortableSecret { Id = id, Kind = material.Kind, Value = material.Value });
			}
		}

		return secrets;
	}

	private byte[]? ReadVariant(IconEntity icon, string variant)
	{
		using var stream = _iconStorage.OpenVariant(icon.PackId, icon.Id, variant);
		if (stream is null)
		{
			return null;
		}

		using var memory = new MemoryStream();
		stream.CopyTo(memory);
		return memory.ToArray();
	}

	private static HashSet<Guid> ReferencedIconIds(IReadOnlyList<string?> widgetData)
	{
		var iconIds = new HashSet<Guid>();
		foreach (var data in widgetData)
		{
			foreach (var guid in GuidReferences.ExtractAll(data))
			{
				iconIds.Add(guid);
			}
		}

		return iconIds;
	}
}
