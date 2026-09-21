namespace MacroDeck.LicenseTool.Model;

internal sealed class ComponentSet
{
	private readonly Dictionary<string, Component> _components = new(StringComparer.Ordinal);

	public IEnumerable<Component> All => _components.Values;

	public Component? Find(Ecosystem ecosystem, string name) =>
		_components.GetValueOrDefault(Component.KeyOf(ecosystem, name));

	public void Add(Component component)
	{
		if (_components.TryGetValue(component.Key, out var existing))
		{
			existing.MergeFrom(component);
		}
		else
		{
			_components.Add(component.Key, component);
		}
	}

	public void Remove(Component component) => _components.Remove(component.Key);
}
