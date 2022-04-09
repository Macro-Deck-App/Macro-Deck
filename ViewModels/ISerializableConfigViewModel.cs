using SuchByte.MacroDeck.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace SuchByte.MacroDeck.ViewModels
{
    public interface ISerializableConfigViewModel
    {
        protected ISerializableConfiguration SerializableConfiguration { get; }

        void SetConfig();

        bool SaveConfig();
    }
}
