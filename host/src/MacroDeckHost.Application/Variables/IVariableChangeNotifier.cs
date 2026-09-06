namespace MacroDeckHost.Application.Variables;

public sealed class VariableChangedEventArgs : EventArgs
{
	public VariableChangedEventArgs(string name) => Name = name;

	/// <summary>The variable's canonical name, without the public <c>vars.</c> prefix.</summary>
	public string Name { get; }
}

/// <summary>
/// In-process fan-out of variable value changes, for host-side readers that render one - a widget UI
/// session showing a metric, which has to repaint the moment the value moves rather than on a poll of its
/// own. The same shape <see cref="Weather.IWeatherStateNotifier" /> uses, and for the same reason: the
/// client-facing broadcast is a single-reader channel, so a second consumer needs its own signal.
/// </summary>
public interface IVariableChangeNotifier
{
	event EventHandler<VariableChangedEventArgs>? Changed;

	void Publish(string name);
}

public sealed class VariableChangeNotifier : IVariableChangeNotifier
{
	public event EventHandler<VariableChangedEventArgs>? Changed;

	public void Publish(string name)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);

		Changed?.Invoke(this, new VariableChangedEventArgs(name));
	}
}
