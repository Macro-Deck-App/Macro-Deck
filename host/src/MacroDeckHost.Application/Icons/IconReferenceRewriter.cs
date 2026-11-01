using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Portable;
using MacroDeckHost.Application.Secrets.Approvals;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using Mediator;

namespace MacroDeckHost.Application.Icons;

public interface IIconReferenceRewriter
{
	Task<int> Replace(Guid fromIconId, Guid toIconId, CancellationToken cancellationToken);
}

public sealed class IconReferenceRewriter(
	IFolderCache folderCache,
	IAutomationCache automationCache,
	IScriptCache scriptCache,
	IMediator mediator,
	WidgetLayoutGate? layoutGate = null) : IIconReferenceRewriter
{
	private readonly WidgetLayoutGate _layoutGate = layoutGate ?? new WidgetLayoutGate();

	public async Task<int> Replace(Guid fromIconId, Guid toIconId, CancellationToken cancellationToken)
	{
		var map = new Dictionary<Guid, Guid> { [fromIconId] = toIconId };
		var approval = SecretApprovalWrite.Rewrite(snapshot => PortableGuidRemapper.Remap(snapshot, map) ?? snapshot);
		var rewritten = 0;

		foreach (var folderId in folderCache.GetAllFolders().Select(folder => folder.Id).ToList())
		{
			using var lease = await _layoutGate.Enter(cancellationToken);
			if (folderCache.GetFolderById(folderId) is not { } folder)
			{
				continue;
			}

			var changedWidgets = new List<WidgetEntity>();
			foreach (var widget in folder.Widgets)
			{
				var data = PortableGuidRemapper.Remap(widget.Data, map);
				if (!string.Equals(data, widget.Data, StringComparison.Ordinal))
				{
					widget.Data = data;
					changedWidgets.Add(widget);
				}
			}

			if (changedWidgets.Count > 0)
			{
				folderCache.UpdateWidgets(folder.Id, changedWidgets, approval);
			}

			var viewConfiguration = PortableGuidRemapper.Remap(folder.ViewConfiguration, map);
			var viewChanged = !string.Equals(viewConfiguration, folder.ViewConfiguration, StringComparison.Ordinal);
			if (viewChanged)
			{
				folder.ViewConfiguration = viewConfiguration;
				await folderCache.AddOrUpdate(folder);
			}

			lease.Dispose();

			foreach (var widget in changedWidgets)
			{
				await mediator.Publish(new WidgetUpdatedNotification(widget, true), cancellationToken);
			}

			rewritten += changedWidgets.Count;

			if (viewChanged)
			{
				await mediator.Publish(new FolderUpdatedNotification(folder), cancellationToken);
				rewritten++;
			}
		}

		foreach (var automation in automationCache.GetAll())
		{
			var flows = PortableGuidRemapper.Remap(automation.Flows, map) ?? automation.Flows;
			if (!string.Equals(flows, automation.Flows, StringComparison.Ordinal))
			{
				automation.Flows = flows;
				automation.UpdatedAt = DateTime.UtcNow;
				await automationCache.AddOrUpdate(automation, approval);
				await mediator.Publish(new AutomationUpdatedNotification(automation), cancellationToken);
				rewritten++;
			}
		}

		foreach (var script in scriptCache.GetAll())
		{
			var flows = PortableGuidRemapper.Remap(script.Flows, map) ?? script.Flows;
			if (!string.Equals(flows, script.Flows, StringComparison.Ordinal))
			{
				script.Flows = flows;
				script.UpdatedAt = DateTime.UtcNow;
				await scriptCache.AddOrUpdate(script, approval);
				await mediator.Publish(new ScriptUpdatedNotification(script), cancellationToken);
				rewritten++;
			}
		}

		return rewritten;
	}
}
