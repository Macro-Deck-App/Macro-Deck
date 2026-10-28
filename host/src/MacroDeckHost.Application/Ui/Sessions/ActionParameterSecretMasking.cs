using System.Text.Json;
using MacroDeck.Sdk.Actions;
using MacroDeckHost.Application.Secrets;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeckHost.Application.Ui.Sessions;

/// <summary>
/// Masks the values of <c>Secret</c>/<c>Password</c>-typed action parameters before they are put on a
/// config surface's <see cref="UiConfigSurfaceAttributes.Parameters" /> attribute - the same sniff
/// <c>ConfigFlowManager.RememberSecretFields</c> uses to classify a config flow step's fields, applied
/// here to an action's declared <see cref="ActionParameter" /> list instead.
/// </summary>
/// <remarks>
/// Expanding a configuration surface is a UI interaction, not an explicit intent to reveal a stored
/// secret - a card expand must not be the thing that discloses a password to the plugin process
/// (in-process or remote alike; there is no privileged shortcut for an in-process action). The plugin
/// still receives the real value through the ordinary save path, when the user actually submits it -
/// nothing here touches that path.
/// </remarks>
internal static class ActionParameterSecretMasking
{
	private static readonly JsonElement _maskedSentinel =
		JsonSerializer.SerializeToElement(UiConfigSurfaceAttributes.MaskedSecretValue);

	public static Dictionary<string, JsonElement> Mask(
		IReadOnlyList<ActionParameter> definitions,
		IReadOnlyDictionary<string, JsonElement>? values)
	{
		var masked = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		if (values is null)
		{
			return masked;
		}

		var byName = DeclaredSecretWalker.Index(definitions);

		foreach (var (name, value) in values)
		{
			// A stored value the action no longer declares is masked rather than passed through: a
			// parameter that was Secret before it was removed still has its value on disk.
			masked[name] = byName.TryGetValue(name, out var definition)
				? DeclaredSecretWalker.Rewrite(definition, value, MaskingVisitor.Instance)
				: _maskedSentinel;
		}

		return masked;
	}

	private sealed class MaskingVisitor : ISecretPositionVisitor
	{
		public static readonly MaskingVisitor Instance = new();

		public JsonElement Secret(JsonElement value)
		{
			return _maskedSentinel;
		}

		public JsonElement Undeclared(JsonElement value)
		{
			return _maskedSentinel;
		}

		public JsonElement Other(JsonElement value)
		{
			return value;
		}
	}
}
