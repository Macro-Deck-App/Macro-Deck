using SuchByte.MacroDeck.ExtensionStore;
using System;
using System.Collections.Generic;
using System.Text;

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
