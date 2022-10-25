using SuchByte.MacroDeck.ExtensionStore;

namespace SuchByte.MacroDeck.Extension
{
    public interface IMacroDeckExtension
    {
        public ExtensionType ExtensionType { get; }
        public string ExtensionTypeDisplayName { get; }
        public object ExtensionObject { get; }
        public bool Configurable { get; }
        public void Uninstall();

    }
}
