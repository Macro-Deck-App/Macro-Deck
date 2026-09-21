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

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
		=> Task.FromResult<IUiSession?>(configSession);
}
