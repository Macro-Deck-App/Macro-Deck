using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Folders;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.Profiles;
using MacroDeckHost.Application.Variables.Colors;
using MacroDeckHost.Application.Widgets;
using TransportProfile = MacroDeckHost.Application.Ui.Transport.Messages.Profiles.Profile;
using TransportProfileLayout = MacroDeckHost.Application.Ui.Transport.Messages.Profiles.ProfileLayout;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Profiles;

public sealed class ProfileRegistry : IProfileRegistry
{
	private readonly IProfileCache _profileCache;
	private readonly IFolderCache _folderCache;
	private readonly IIntegrationRegistry _integrations;
	private readonly IWidgetTypeRegistry _widgetTypes;
	private readonly DeviceLayoutConstraintTracker _layoutConstraints;
	private readonly ILogger _logger;
	private readonly IColorReferenceResolver _colors;

	public ProfileRegistry(
		IProfileCache profileCache,
		IFolderCache folderCache,
		IIntegrationRegistry integrations,
		IWidgetTypeRegistry widgetTypes,
		DeviceLayoutConstraintTracker layoutConstraints,
		ILogger logger,
		IColorReferenceResolver colors)
	{
		_colors = colors;
		_profileCache = profileCache;
		_folderCache = folderCache;
		_integrations = integrations;
		_widgetTypes = widgetTypes;
		_layoutConstraints = layoutConstraints;
		_logger = logger;
	}

	public IReadOnlyList<TransportProfile> GetProfiles()
	{
		var profiles = ProfileOrdering.Sort(_profileCache.GetAll())
			.Select(entity => ProfileDtoMapper.MapJsonProfile(entity,
				_layoutConstraints.Get(entity.Id.ToString()),
				_colors))
			.ToList();

		var nextOrder = profiles.Count > 0 ? profiles.Max(p => p.Order) + 1 : 0;

		foreach (var integration in EnabledProviders())
		{
			var provider = (IProfileProvider)integration;
			foreach (var descriptor in provider.GetProfiles())
			{
				if (!TryQualify(integration.Id, descriptor.Id, "profile", out var profileId))
				{
					continue;
				}

				profiles.Add(MapVirtualProfile(integration.Id, profileId, descriptor, nextOrder++));
			}
		}

		return profiles;
	}

	public IReadOnlyList<Folder> GetFoldersForProfile(string profileId)
	{
		if (TryResolveVirtual(profileId, out var integrationId, out var localProfileId))
		{
			return GetVirtualFolders(integrationId, localProfileId);
		}

		if (!Guid.TryParse(profileId, out var id))
		{
			return [];
		}

		return _folderCache.GetFoldersByProfileId(id)
			.Select(folder => FolderDtoMapper.MapToDto(folder, _colors))
			.ToList();
	}

	public bool IsVirtual(string profileId)
	{
		return QualifiedId.IsQualified(profileId);
	}

	public Widget? FindVirtualWidget(string widgetId)
	{
		if (!VirtualWidgetIds.TryParse(widgetId,
				out var integrationId,
				out var localProfileId,
				out var localFolderId,
				out var localWidgetId)
			|| EnabledProviders().FirstOrDefault(i => i.Id == integrationId) is not IProfileProvider provider)
		{
			return null;
		}

		var widget = provider.GetProfiles()
			.FirstOrDefault(p => p.Id == localProfileId)
			?.Folders.FirstOrDefault(f => f.Id == localFolderId)
			?.Widgets.FirstOrDefault(w => w.Id == localWidgetId);

		return widget is null ? null : MapVirtualWidget(integrationId, localProfileId, localFolderId, widget);
	}

	public Task<bool> RouteWidgetInteraction(string folderId, string widgetId, WidgetInteraction interaction)
	{
		if (!VirtualWidgetIds.TryParse(widgetId,
			out var integrationId,
			out var localProfileId,
			out var localFolderId,
			out var localWidgetId))
		{
			return Task.FromResult(false);
		}

		var integration = EnabledProviders().FirstOrDefault(i => i.Id == integrationId);
		if (integration is not IProfileProvider provider)
		{
			return Task.FromResult(false);
		}

		return RouteAsync(provider, localProfileId, localFolderId, localWidgetId, interaction);
	}

	private static async Task<bool> RouteAsync(
		IProfileProvider provider,
		string profileId,
		string folderId,
		string widgetId,
		WidgetInteraction interaction)
	{
		await provider.HandleWidgetInteractionAsync(profileId, folderId, widgetId, interaction);
		return true;
	}

	private List<Folder> GetVirtualFolders(string integrationId, string localProfileId)
	{
		var integration = EnabledProviders().FirstOrDefault(i => i.Id == integrationId);
		if (integration is not IProfileProvider provider)
		{
			return [];
		}

		var descriptor = provider.GetProfiles().FirstOrDefault(p => p.Id == localProfileId);
		if (descriptor is null)
		{
			return [];
		}

		if (!TryQualify(integrationId, descriptor.Id, "profile", out var profileId))
		{
			return [];
		}

		return descriptor.Folders
			.OrderBy(f => f.Order)
			.Select(folder => MapVirtualFolder(integrationId, profileId, descriptor, folder))
			.OfType<Folder>()
			.ToList();
	}

	private Folder? MapVirtualFolder(
		string integrationId,
		string profileId,
		VirtualProfileDescriptor profile,
		VirtualFolderDescriptor folder)
	{
		if (!TryQualify(integrationId, folder.Id, "folder", out var folderId))
		{
			return null;
		}

		string? parentId = null;
		if (folder.ParentId is not null)
		{
			if (!TryQualify(integrationId, folder.ParentId, "folder", out var qualifiedParentId))
			{
				return null;
			}

			parentId = qualifiedParentId;
		}

		var dto = new Folder
		{
			Id = folderId,
			Name = folder.Name,
			ProfileId = profileId,
			ParentId = parentId,
			Order = folder.Order,
			Rows = profile.Layout.Rows,
			Columns = profile.Layout.Columns
		};

		dto.Widgets.AddRange(folder.Widgets.Select(w => MapVirtualWidget(integrationId, profile.Id, folder.Id, w))
			.OfType<Widget>());
		return dto;
	}

	private Widget? MapVirtualWidget(
		string integrationId,
		string localProfileId,
		string localFolderId,
		VirtualWidgetDescriptor widget)
	{
		if (!TryQualifyWidget(integrationId, localProfileId, localFolderId, widget.Id, out var widgetId))
		{
			return null;
		}

		return new Widget
		{
			Id = widgetId,
			// An unresolvable type passes through rather than becoming an ActionButton. A virtual profile
			// is re-derived on every fetch, so coercing here turned a type whose provider had simply not
			// connected yet into a button with data no button can read - and did it repeatedly.
			Type = _widgetTypes.Resolve(widget.Type) ?? widget.Type,
			PositionX = widget.PositionX,
			PositionY = widget.PositionY,
			Width = widget.Width,
			Height = widget.Height,
			Data = widget.Data
		};
	}

	private static TransportProfile MapVirtualProfile(
		string integrationId,
		string profileId,
		VirtualProfileDescriptor descriptor,
		int order)
	{
		return new TransportProfile
		{
			Id = profileId,
			Name = descriptor.Name,
			Order = order,
			LayoutType = descriptor.Layout.Kind.ToString(),
			IsVirtual = true,
			SourceIntegrationId = integrationId,
			Layout = new TransportProfileLayout
			{
				Rows = descriptor.Layout.Rows,
				Columns = descriptor.Layout.Columns,
				RowsLocked = descriptor.Layout.RowsLocked,
				ColumnsLocked = descriptor.Layout.ColumnsLocked
			},
			DefaultRows = descriptor.Layout.Rows,
			DefaultColumns = descriptor.Layout.Columns,
			DefaultBackgroundColor = null,
			DefaultWidgetSpacing = null,
			DefaultWidgetBorderRadius = null
		};
	}

	private static bool TryResolveVirtual(string id, out string integrationId, out string localId)
	{
		if (!QualifiedId.TryParse(id, out var qualifiedId))
		{
			integrationId = string.Empty;
			localId = string.Empty;
			return false;
		}

		integrationId = qualifiedId.OwnerId;
		localId = qualifiedId.LocalId;
		return true;
	}

	private bool TryQualifyWidget(
		string integrationId,
		string localProfileId,
		string localFolderId,
		string localWidgetId,
		out string qualified)
	{
		if (VirtualWidgetIds.TryCreate(integrationId, localProfileId, localFolderId, localWidgetId, out qualified))
		{
			return true;
		}

		_logger.Warning("Skipping virtual widget '{LocalId}' of integration '{IntegrationId}': not a usable id",
			localWidgetId,
			integrationId);
		return false;
	}

	private bool TryQualify(string integrationId, string localId, string kind, out string qualified)
	{
		if (QualifiedId.TryCreate(integrationId, localId, LocalIdKind.Resource, out var id))
		{
			qualified = id.ToString();
			return true;
		}

		_logger.Warning("Skipping virtual {Kind} of integration '{IntegrationId}': '{LocalId}' is not a usable id",
			kind,
			integrationId,
			localId);
		qualified = string.Empty;
		return false;
	}

	private IEnumerable<IIntegration> EnabledProviders()
	{
		return _integrations.Integrations
			.Where(integration => integration is IProfileProvider && _integrations.IsEnabled(integration.Id));
	}
}
