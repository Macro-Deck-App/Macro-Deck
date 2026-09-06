namespace MacroDeckHost.Application.Ui.Transport.Messages.ConfigFlow;

using global::System.Text.Json;

public class StartConfigFlowResponse
{
	public bool Supported { get; set; }

	public string? FlowId { get; set; }

	public ConfigFlowStepDto? Step { get; set; }

	/// <summary>Whether this flow can render itself as a Macro Deck UI tree instead of <see cref="Step" />.
	/// <see cref="Step" /> is still carried either way - a client that cannot render a tree walks it
	/// unchanged, and its fields remain how the host learns which submitted values are secret.</summary>
	public bool SupportsConfigUi { get; set; }

	/// <summary>The UI model major the tree would be built at, meaningful only when
	/// <see cref="SupportsConfigUi" /> is set.</summary>
	public int ConfigUiModelVersion { get; set; }

	public Dictionary<string, JsonElement> InitialValues { get; set; } = new();

	public List<string> StoredSecretFields { get; set; } = new();

	public TransportError? Error { get; set; }
}
