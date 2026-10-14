using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Plugin.Hosting.Tests.PreviewFixture;

public sealed class PreviewingIntegration(IUiSession configSession) : IPluginIntegration, IUiProvider
{
	public IReadOnlyList<IActionDefinition> Actions => [];

	public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
	[
		new()
			{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive }
	];

	public Task InitializeAsync(IIntegrationContext context)
	{
		return Task.CompletedTask;
	}

	public Task ShutdownAsync()
	{
		return Task.CompletedTask;
	}

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		return Task.FromResult<IUiSession?>(configSession);
	}
}
