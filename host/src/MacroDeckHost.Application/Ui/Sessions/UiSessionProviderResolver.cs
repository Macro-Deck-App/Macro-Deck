using MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui;
using MacroDeckHost.Application.Ui.Sessions.InProcess;

namespace MacroDeckHost.Application.Ui.Sessions;

public sealed class UiSessionProviderResolver : IUiSessionProviderResolver
{
	private readonly RemoteUiProviderRegistry _remote;
	private readonly UiProviderRegistry _inProcess;
	private readonly ConfigFlowUiProviderRegistry _configFlow;
	private readonly ActionConfigUiProviderRegistry _actionConfig;
	private readonly WidgetUiProviderRegistry _widget;
	private readonly IntegrationUiProviderRegistry _integrationUi;
	private readonly UiPreviewProviderRegistry _preview;

	public UiSessionProviderResolver(
		RemoteUiProviderRegistry remote,
		UiProviderRegistry inProcess,
		ConfigFlowUiProviderRegistry configFlow,
		ActionConfigUiProviderRegistry actionConfig,
		WidgetUiProviderRegistry widget,
		IntegrationUiProviderRegistry integrationUi,
		UiPreviewProviderRegistry preview)
	{
		_remote = remote;
		_inProcess = inProcess;
		_configFlow = configFlow;
		_actionConfig = actionConfig;
		_widget = widget;
		_integrationUi = integrationUi;
		_preview = preview;
	}

	// The remote registry is asked first: a plugin id that reaches here names a plugin, and only the
	// in-process registry's RemotePluginIntegration exclusion keeps the two from both answering. The
	public IUiSessionProvider? Resolve(string providerId)
		=> _remote.Resolve(providerId) ??
			_inProcess.Resolve(providerId) ??
			_configFlow.Resolve(providerId) ??
			_actionConfig.Resolve(providerId) ??
			_widget.Resolve(providerId) ??
			_integrationUi.Resolve(providerId) ??
			_preview.Resolve(providerId);
}
