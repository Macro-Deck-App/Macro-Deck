using System.Text.Json.Nodes;

namespace MacroDeckHost.Widgets.ActionButton;

internal sealed class ActionButtonLabelClipboard
{
	private readonly Lock _sync = new();
	private JsonObject? _label;

	public bool HasLabel
	{
		get
		{
			lock (_sync)
			{
				return _label is not null;
			}
		}
	}

	public void Copy(JsonObject label)
	{
		ArgumentNullException.ThrowIfNull(label);

		lock (_sync)
		{
			_label = (JsonObject)label.DeepClone();
		}
	}

	public JsonObject? Paste()
	{
		lock (_sync)
		{
			return (JsonObject?)_label?.DeepClone();
		}
	}
}
