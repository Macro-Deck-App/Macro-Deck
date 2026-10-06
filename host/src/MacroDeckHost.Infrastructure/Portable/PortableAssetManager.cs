using System.Text.Json;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Scripts;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Rendering;
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

	// Icons and content.json share the encrypted payload cap with the fonts.
	private const long FontExportBudget = 48L * 1024 * 1024;

	private readonly IIconPackCache _iconPackCache;
	private readonly IIconStorage _iconStorage;
	private readonly ISecretService _secretService;
	private readonly IScriptService _scriptService;
	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly IWidgetVariableCloner _variableCloner;
	private readonly IMediator _mediator;
	private readonly IAppPreferenceService _preferences;
	private readonly ILocalizationResolver _localization;
	private readonly IFontCatalog _fontCatalog;
	private readonly IUserFontLibrary _userFonts;
	private readonly IIconPackOwnerRegistry _ownerRegistry;
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
		IFontCatalog fontCatalog,
		IUserFontLibrary userFonts,
		IIconPackOwnerRegistry ownerRegistry,
		ILogger logger)
	{
		_fontCatalog = fontCatalog;
		_ownerRegistry = ownerRegistry;
		_userFonts = userFonts;
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
		var bundledFiles = new HashSet<Guid>();
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

			var fileHashes = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[IconVariants.Master] = ContentHash.Compute(master)
			};
			if (bundledFiles.Add(icon.Id))
			{
				files.Add(new PortableIconFile(icon.Id, IconVariants.Master, master));
			}

			var appearances = icon.AppearanceOfId is null
				? CollectAppearances(icon, files, bundledFiles)
				: [];
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
				Appearances = appearances.Count > 0 ? appearances : null
			});
		}

		var secrets = options.IncludeSecrets ? await CollectSecrets(referencingData) : [];
		var variables = await CollectVariables(widgets);
		var culture = (await _preferences.GetLocalization()).Culture;
		var (fonts, fontFiles) = CollectFonts(referencingData);
		return new PortableAssetBundle(icons,
			files,
			scripts,
			secrets,
			CollectIntegrations(referencingData, culture),
			variables,
			fonts,
			fontFiles);
	}

	private List<PortableIconAppearance> CollectAppearances(IconEntity icon,
		List<PortableIconFile> files,
		HashSet<Guid> bundledFiles)
	{
		var appearances = new List<PortableIconAppearance>();
		foreach (var asset in _iconPackCache.GetAppearances(icon.Id))
		{
			if (asset.ProcessingState != IconProcessingState.Ready || asset.AppearanceTraits is not { } traits)
			{
				continue;
			}

			var master = ReadVariant(asset, IconVariants.Master);
			if (master is null)
			{
				_logger.Warning("Skipping appearance {AppearanceId} during export: master variant missing", asset.Id);
				continue;
			}

			if (bundledFiles.Add(asset.Id))
			{
				files.Add(new PortableIconFile(asset.Id, IconVariants.Master, master));
			}

			appearances.Add(new PortableIconAppearance
			{
				Id = asset.Id,
				Traits = new Dictionary<string, string>(traits, StringComparer.Ordinal),
				Width = asset.Width,
				Height = asset.Height,
				IsAnimated = asset.IsAnimated,
				FrameCount = asset.FrameCount,
				SourceContentHash = asset.SourceContentHash ?? asset.DeclaredSourceContentHash,
				FileContentHashes = new Dictionary<string, string>(StringComparer.Ordinal)
				{
					[IconVariants.Master] = ContentHash.Compute(master)
				},
				OriginalFormat = asset.OriginalFormat
			});
		}

		return appearances;
	}

	private (List<PortableFont> Fonts, List<PortableFontFile> Files) CollectFonts(IReadOnlyList<string?> referencingData)
	{
		var userFaces = _fontCatalog.GetFaces()
			.Where(face => face is { UserImported: true, ContentHash: not null })
			.ToDictionary(face => face.FaceId, StringComparer.Ordinal);
		var fonts = new List<PortableFont>();
		var files = new List<PortableFontFile>();
		if (userFaces.Count == 0)
		{
			return (fonts, files);
		}

		var byHash = new Dictionary<string, PortableFont>(StringComparer.Ordinal);
		var budget = FontExportBudget;
		foreach (var reference in referencingData.SelectMany(JsonStringValues).Distinct(StringComparer.Ordinal))
		{
			if (!userFaces.TryGetValue(_fontCatalog.ResolveFaceId(reference), out var face))
			{
				continue;
			}

			if (!byHash.TryGetValue(face.ContentHash!, out var font))
			{
				var file = _userFonts.ReadFile(UserFontFiles.FontIdOf(face.ContentHash!));
				if (file is null || file.Bytes.LongLength > budget)
				{
					_logger.Warning("Skipping font {FaceId} during export: file missing or over the size budget", face.FaceId);
					continue;
				}

				budget -= file.Bytes.LongLength;
				font = new PortableFont
				{
					FontId = file.FontId,
					Format = file.Format,
					ContentHash = face.ContentHash!,
					Family = face.Family,
					StyleName = face.StyleName
				};
				byHash[face.ContentHash!] = font;
				fonts.Add(font);
				files.Add(new PortableFontFile(file.FontId, file.Format, file.Bytes));
			}

			foreach (var id in new[] { reference, face.FaceId })
			{
				if (!font.FaceIds.Contains(id))
				{
					font.FaceIds.Add(id);
				}
			}
		}

		return (fonts, files);
	}

	private static IEnumerable<string> JsonStringValues(string? data)
	{
		if (string.IsNullOrWhiteSpace(data))
		{
			return [];
		}

		JsonNode? root;
		try
		{
			root = JsonNode.Parse(data);
		}
		catch (JsonException)
		{
			return [];
		}

		var values = new List<string>();
		var pending = new Stack<JsonNode?>([root]);
		while (pending.TryPop(out var node))
		{
			switch (node)
			{
				case JsonObject obj:
					foreach (var (_, child) in obj)
					{
						pending.Push(child);
					}

					break;
				case JsonArray array:
					foreach (var child in array)
					{
						pending.Push(child);
					}

					break;
				case JsonValue value when value.TryGetValue<string>(out var text):
					values.Add(text);
					break;
			}
		}

		return values;
	}

	private static bool MatchesRecordedHash(PortableFont font, PortableFontFile file)
	{
		var hash = ContentHash.Compute(file.Bytes);
		return hash == font.ContentHash && UserFontFiles.FontIdOf(hash) == font.FontId;
	}

	private async Task ImportFonts(PortableContent content,
		IReadOnlyList<PortableFontFile> fontFiles,
		CancellationToken cancellationToken)
	{
		if (content.Fonts.Count == 0 || fontFiles.Count == 0)
		{
			return;
		}

		var filesById = fontFiles
			.GroupBy(file => file.FontId, StringComparer.Ordinal)
			.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
		var verified = new List<(PortableFont Font, PortableFontFile File)>();
		foreach (var font in content.Fonts)
		{
			// The file name and the recorded hash are archive claims, so both are checked against the bytes.
			if (!filesById.TryGetValue(font.FontId, out var file) || !MatchesRecordedHash(font, file))
			{
				_logger.Warning("Skipping font {FontId} during import: file missing or does not match its hash", font.FontId);
				continue;
			}

			verified.Add((font, file));
		}

		if (verified.Count == 0)
		{
			return;
		}

		try
		{
			await _userFonts.Import(verified
					.Select(entry => new UserFontUpload($"{entry.File.FontId}.{entry.File.Format}", entry.File.Bytes))
					.ToList(),
				cancellationToken);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			_logger.Warning(exception, "Skipping the bundled fonts during import: they could not be stored");
			return;
		}

		var localFaces = _fontCatalog.GetFaces();
		var listedIds = localFaces.Select(face => face.FaceId).ToHashSet(StringComparer.Ordinal);
		var renames = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (font, file) in verified)
		{
			var inspected = UserFontFiles.Inspect(file.Bytes);
			if (inspected is null)
			{
				continue;
			}

			var slant = inspected.Slant switch
			{
				SkiaSharp.SKFontStyleSlant.Italic => FontFaceIdentity.ItalicSlant,
				SkiaSharp.SKFontStyleSlant.Oblique => FontFaceIdentity.ObliqueSlant,
				_ => FontFaceIdentity.UprightSlant
			};
			var local = localFaces.FirstOrDefault(face =>
				string.Equals(face.Family, inspected.Family, StringComparison.OrdinalIgnoreCase) &&
				face.Weight == inspected.Weight &&
				face.Width == inspected.Width &&
				face.Slant == slant);
			if (local is null)
			{
				continue;
			}

			foreach (var faceId in font.FaceIds.Where(id => !listedIds.Contains(id) && id != local.FaceId))
			{
				renames.TryAdd(faceId, local.FaceId);
			}
		}

		// Only the imported content is rewritten, so an archive can never redirect existing widgets.
		RewriteFaceIds(content, renames);
	}

	private static void RewriteFaceIds(PortableContent content, Dictionary<string, string> renames)
	{
		if (renames.Count == 0)
		{
			return;
		}

		foreach (var widget in content.Widgets ?? [])
		{
			widget.Data = RewriteFaceIds(widget.Data, renames);
		}

		foreach (var widget in (content.Folders ?? []).Concat(content.Profile?.Folders ?? []).SelectMany(folder => folder.Widgets))
		{
			widget.Data = RewriteFaceIds(widget.Data, renames);
		}

		foreach (var script in content.Scripts)
		{
			script.Flows = RewriteFaceIds(script.Flows, renames) ?? string.Empty;
		}
	}

	private static string? RewriteFaceIds(string? json, Dictionary<string, string> renames)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return json;
		}

		try
		{
			var root = JsonNode.Parse(json);
			return root is not null && Rewrite(root, renames) ? root.ToJsonString() : json;
		}
		catch (JsonException)
		{
			return json;
		}
	}

	private static bool Rewrite(JsonNode node, Dictionary<string, string> renames)
	{
		var changed = false;
		switch (node)
		{
			case JsonObject obj:
				foreach (var key in obj.Select(property => property.Key).ToList())
				{
					changed |= RewriteChild(obj[key], replacement => obj[key] = replacement, renames);
				}

				break;
			case JsonArray array:
				for (var index = 0; index < array.Count; index++)
				{
					var position = index;
					changed |= RewriteChild(array[index], replacement => array[position] = replacement, renames);
				}

				break;
		}

		return changed;
	}

	private static bool RewriteChild(JsonNode? child, Action<string> replace, Dictionary<string, string> renames)
	{
		if (child is JsonValue value && value.TryGetValue<string>(out var text) && renames.TryGetValue(text, out var renamed))
		{
			replace(renamed);
			return true;
		}

		return child is not null and not JsonValue && Rewrite(child, renames);
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
					Value = snapshot.FileSource is null ? snapshot.Value : string.Empty,
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
				RequiresConfiguration = integration.RequiresConfiguration()
			})
			.ToList();
	}

	public async Task<IReadOnlyDictionary<Guid, Guid>> Import(PortableContent content,
		IReadOnlyList<PortableIconFile> iconFiles,
		IReadOnlyList<PortableFontFile> fontFiles,
		CancellationToken cancellationToken)
	{
		await ImportFonts(content, fontFiles, cancellationToken);
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
		var newAppearances = new List<IconEntity>();
		var reusedWithNewAppearances = new List<(IconEntity Parent, List<IconEntity> Added)>();
		var reused = 0;
		foreach (var portableIcon in importable)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var variants = filesByIcon[portableIcon.Id];
			if (!VerifyBundledFiles(portableIcon.Id, portableIcon.FileContentHashes, variants))
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
				var missing = await MissingAppearances(existing, portableIcon, filesByIcon, cancellationToken);
				if (missing.Count > 0)
				{
					reusedWithNewAppearances.Add((existing, missing));
				}

				continue;
			}

			if (isNewPack && !packCreated)
			{
				await _iconPackCache.AddOrUpdatePack(pack);
				packCreated = true;
			}

			var newId = Guid.CreateVersion7();
			await _iconStorage.WriteVariant(pack.Id, newId, IconVariants.Master, masterBytes, cancellationToken);

			var newIcon = new IconEntity
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
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			};
			newIcons.Add(newIcon);
			newAppearances.AddRange(await ImportAppearances(newIcon, portableIcon, filesByIcon, cancellationToken));

			idMap[portableIcon.Id] = newId;
			importedThisRun[masterHash.Value] = newId;
		}

		_logger.Information("Imported {Created} icon(s) and reused {Reused} already present", newIcons.Count, reused);
		await AddReusedIconAppearances(reusedWithNewAppearances, cancellationToken);
		if (newIcons.Count == 0)
		{
			return;
		}

		await _iconPackCache.AddIcons(pack.Id, [.. newIcons, .. newAppearances]);
		if (isNewPack)
		{
			await _mediator.Publish(new IconPackCreatedNotification(pack, newIcons.Count), cancellationToken);
		}
		else
		{
			await _mediator.Publish(new IconsAddedNotification(BatchId: null, pack.Id, newIcons), cancellationToken);
		}
	}

	private bool VerifyBundledFiles(Guid iconId,
		Dictionary<string, string> fileContentHashes,
		List<PortableIconFile> variants)
	{
		foreach (var variant in variants)
		{
			var declared = ContentHash.Normalize(fileContentHashes.GetValueOrDefault(variant.Variant));
			if (declared is null || declared == ContentHash.Compute(variant.Bytes))
			{
				continue;
			}

			_logger.Warning("Skipping icon {IconId} from archive: bundled {Variant} fails its declared hash",
				iconId,
				variant.Variant);
			return false;
		}

		return true;
	}

	private async Task<List<IconEntity>> MissingAppearances(IconEntity existing,
		PortableIcon portableIcon,
		Dictionary<Guid, List<PortableIconFile>> filesByIcon,
		CancellationToken cancellationToken)
	{
		if (portableIcon.Appearances is not { Count: > 0 } ||
			existing.AppearanceOfId is not null ||
			_iconPackCache.GetPackById(existing.PackId) is not { } pack ||
			_ownerRegistry.IsReadOnly(pack))
		{
			return [];
		}

		return await ImportAppearances(existing, portableIcon, filesByIcon, cancellationToken);
	}

	private async Task AddReusedIconAppearances(List<(IconEntity Parent, List<IconEntity> Added)> additions,
		CancellationToken cancellationToken)
	{
		foreach (var pack in additions.GroupBy(addition => addition.Parent.PackId))
		{
			await _iconPackCache.AddIcons(pack.Key, pack.SelectMany(addition => addition.Added).ToList());
		}

		foreach (var (parent, _) in additions)
		{
			await _mediator.Publish(new IconUpdatedNotification(parent), cancellationToken);
		}
	}

	private async Task<List<IconEntity>> ImportAppearances(IconEntity parent,
		PortableIcon portableIcon,
		Dictionary<Guid, List<PortableIconFile>> filesByIcon,
		CancellationToken cancellationToken)
	{
		var keys = _iconPackCache.GetAppearances(parent.Id)
			.Select(asset => asset.AppearanceTraits is { } traits ? IconAppearanceTraits.ToKey(traits) : string.Empty)
			.ToHashSet(StringComparer.Ordinal);
		var added = new List<IconEntity>();
		foreach (var appearance in portableIcon.Appearances ?? [])
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (keys.Count >= IconAppearanceTraits.MaxAppearancesPerIcon)
			{
				break;
			}

			if (!IconAppearanceTraits.IsValid(appearance.Traits) ||
				keys.Contains(IconAppearanceTraits.ToKey(appearance.Traits)) ||
				!filesByIcon.TryGetValue(appearance.Id, out var variants) ||
				variants.FirstOrDefault(variant => variant.Variant == IconVariants.Master) is not { } master ||
				!VerifyBundledFiles(appearance.Id, appearance.FileContentHashes, variants))
			{
				continue;
			}

			var assetId = Guid.CreateVersion7();
			await _iconStorage.WriteVariant(parent.PackId, assetId, IconVariants.Master, master.Bytes, cancellationToken);
			keys.Add(IconAppearanceTraits.ToKey(appearance.Traits));
			added.Add(new IconEntity
			{
				Id = assetId,
				PackId = parent.PackId,
				Name = parent.Name,
				Width = appearance.Width,
				Height = appearance.Height,
				IsAnimated = appearance.IsAnimated,
				FrameCount = appearance.FrameCount,
				MasterContentHash = MasterContentHash.Compute(master.Bytes).Value,
				DeclaredSourceContentHash = ContentHash.Normalize(appearance.SourceContentHash),
				OriginalFormat = appearance.OriginalFormat,
				ProcessingState = IconProcessingState.Ready,
				AppearanceOfId = parent.Id,
				AppearanceTraits = new Dictionary<string, string>(appearance.Traits, StringComparer.Ordinal),
				CreatedAt = DateTime.UtcNow,
				UpdatedAt = DateTime.UtcNow
			});
		}

		return added;
	}

	private async Task<IconEntity?> FindByBackfilledMasterHash(MasterContentHash hash,
		CancellationToken cancellationToken)
	{
		foreach (var candidate in _iconPackCache.GetIconsMissingMasterContentHash()
			.Where(icon => icon.AppearanceOfId is null))
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
