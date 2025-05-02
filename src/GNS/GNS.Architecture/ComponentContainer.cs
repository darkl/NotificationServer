using System.Collections.ObjectModel;

namespace GNS.Architecture;

public class ComponentContainer : Collection<IComponent>, IComponentContainer
{
    private readonly Dictionary<string, IComponent> _componentsByName = new Dictionary<string, IComponent>();

    protected override void InsertItem(int index, IComponent item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        if (string.IsNullOrEmpty(item.UniqueName))
            throw new ArgumentException("Component must have a non-empty UniqueName", nameof(item));

        if (_componentsByName.ContainsKey(item.UniqueName))
            throw new InvalidOperationException($"Component with name '{item.UniqueName}' already exists in container");

        base.InsertItem(index, item);
        _componentsByName.Add(item.UniqueName, item);
    }

    protected override void RemoveItem(int index)
    {
        IComponent item = this[index];
        _componentsByName.Remove(item.UniqueName);
        base.RemoveItem(index);
    }

    protected override void ClearItems()
    {
        _componentsByName.Clear();
        base.ClearItems();
    }

    public void RemoveByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));

        if (_componentsByName.TryGetValue(name, out IComponent component))
        {
            Remove(component);
        }
    }

    public IComponent GetByName(string name)
    {
        if (string.IsNullOrEmpty(name))
            throw new ArgumentNullException(nameof(name));

        return _componentsByName.TryGetValue(name, out IComponent component) ? component : null;
    }
}