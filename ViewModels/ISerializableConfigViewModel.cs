using SuchByte.MacroDeck.Models;

namespace SuchByte.MacroDeck.ViewModels
{
    public interface ISerializableConfigViewModel
    {
        protected ISerializableConfiguration SerializableConfiguration { get; }

        void SetConfig();

        bool SaveConfig();
    }
}
