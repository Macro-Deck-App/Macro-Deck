using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Events;

/// <summary>
/// Deleting a widget must close whatever is looking at it - a config editor left open on it is not a
/// view that can ever come back up to date, unlike a reconfigure, which only asks a session to reopen.
/// </summary>
[TestFixture]
public class WidgetDeletedNotificationHandlerTests
{
	[Test]
	public async Task Handle_closes_the_deleted_widgets_ui_sessions()
	{
		var sessions = new RecordingUiSessionBroker();
		var widgetUiProviders = new WidgetUiProviderRegistry(new UnusedFolderCache(),
			[],
			() => null!,
			Serilog.Core.Logger.None);
		var handler = new WidgetDeletedNotificationHandler(new RecordingTransport(), sessions, widgetUiProviders);
		var widgetId = Guid.NewGuid();

		await handler.Handle(new WidgetDeletedNotification(widgetId, Guid.NewGuid()), CancellationToken.None);

		Assert.That(sessions.ClosedWidgets, Does.Contain(widgetId));
	}

	private sealed class UnusedFolderCache : IFolderCache
	{
		public Task InitializeCache() => throw new NotSupportedException();

		public FolderEntity? GetFolderById(Guid id) => throw new NotSupportedException();

		public List<FolderEntity> GetAllFolders() => throw new NotSupportedException();

		public List<FolderEntity> GetFoldersByParentId(Guid? parentId) => throw new NotSupportedException();

		public List<FolderEntity> GetFoldersByProfileId(Guid profileId) => throw new NotSupportedException();

		public Task AddOrUpdate(FolderEntity folder) => throw new NotSupportedException();

		public Task AddOrUpdateRange(IReadOnlyCollection<FolderEntity> folders) => throw new NotSupportedException();

		public Task<FolderSubtreeRemoval> RemoveSubtree(Guid rootId) => throw new NotSupportedException();

		public void AddWidget(Guid folderId, WidgetEntity widget) => throw new NotSupportedException();

		public void AddWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets) => throw new NotSupportedException();

		public void UpdateWidget(Guid folderId, WidgetEntity widget) => throw new NotSupportedException();

		public void UpdateWidgets(Guid folderId, IReadOnlyList<WidgetEntity> widgets)
			=> throw new NotSupportedException();

		public void UpdateWidgetPositions(Guid folderId, IReadOnlyList<WidgetPlacement> placements)
			=> throw new NotSupportedException();

		public void RemoveWidget(Guid folderId, Guid widgetId) => throw new NotSupportedException();

		public void RemoveWidgets(Guid folderId, IReadOnlyList<Guid> widgetIds) => throw new NotSupportedException();

		public void ReplaceWidgets(Guid folderId, IReadOnlyList<Guid> removeIds, IReadOnlyList<WidgetEntity> addWidgets)
			=> throw new NotSupportedException();
	}

	private sealed class RecordingTransport : IUiTransport
	{
		public Task Send<T>(T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
			where T : class
			=> Task.CompletedTask;

		public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;

		public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
			=> Task.CompletedTask;
	}
}
