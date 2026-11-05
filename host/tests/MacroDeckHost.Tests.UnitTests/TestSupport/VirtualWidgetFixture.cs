using System.Text.Json;
using MacroDeck.Sdk.Profiles;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Sdk.Ui;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Widgets;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class VirtualWidgetFixture
{
	public const string IntegrationId = "com.example.volume-mixer";

	public VirtualWidgetFixture(string widgetType, string? data)
	{
		Integration = new FakeProfileProviderIntegration(IntegrationId,
			new VirtualProfileDescriptor("mixer",
				"Mixer",
				ProfileLayout.Grid(1, 1),
				[
					new VirtualFolderDescriptor("page-1",
						"Page 1",
						[new VirtualWidgetDescriptor("app-0", widgetType, 0, 0, Data: data)])
				]));
		Integrations = new ConfigurableIntegrationRegistry([Integration]);
		Profiles = new ProfileRegistry(new EmptyProfileCache(),
			new FakeFolderCache(),
			Integrations,
			new WidgetTypeRegistry(new RecordingMediator()),
			TestDeviceLayoutConstraintTracker.Build(),
			Serilog.Core.Logger.None,
			TestColors.None);
		Press = new VirtualWidgetPress(() => Profiles, Transport, Serilog.Core.Logger.None);
		WidgetId = Profiles.GetFoldersForProfile(IntegrationId + "::mixer").Single().Widgets.Single().Id;
	}

	public FakeProfileProviderIntegration Integration { get; }

	public ConfigurableIntegrationRegistry Integrations { get; }

	public ProfileRegistry Profiles { get; }

	public StatusRecordingTransport Transport { get; } = new();

	public VirtualWidgetPress Press { get; }

	public string WidgetId { get; }

	public UiSessionRequest Request(string widgetType, string? data)
	{
		return new UiSessionRequest
		{
			UiModelVersion = 1,
			Surface = new UiSurface
			{
				Kind = UiSurfaceKinds.Widget,
				SessionMode = UiSessionModes.Shared,
				Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
				{
					[UiWidgetSurfaceAttributes.WidgetId] = JsonSerializer.SerializeToElement(WidgetId),
					[UiWidgetSurfaceAttributes.WidgetType] = JsonSerializer.SerializeToElement(widgetType),
					[UiWidgetSurfaceAttributes.Data] = JsonDocument.Parse(data ?? "{}").RootElement.Clone()
				}
			}
		};
	}
}

internal sealed class StatusRecordingTransport : IUiTransport
{
	public List<(string Group, ActionExecutionStatusEvent Status)> Statuses { get; } = [];

	public Task Send<T>(T message, CancellationToken cancellationToken = default)
		where T : class
	{
		return Task.CompletedTask;
	}

	public Task SendToGroup<T>(string group, T message, CancellationToken cancellationToken = default)
		where T : class
	{
		if (message is ActionExecutionStatusEvent status)
		{
			lock (Statuses)
			{
				Statuses.Add((group, status));
			}
		}

		return Task.CompletedTask;
	}

	public Task SendToConnection<T>(string connectionId, T message, CancellationToken cancellationToken = default)
		where T : class
	{
		return Task.CompletedTask;
	}

	public Task AddToGroup(string connectionId, string group, CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}

	public Task RemoveFromGroup(string connectionId, string group, CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}
}
