using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Text.Json.Serialization;

namespace SuchByte.MacroDeck.Icons
{
    public class IconPack
    {
        /// <summary>
        /// Name of the icon pack
        /// </summary>
        public string Name;

        /// <summary>
        /// Author of the icon pack
        /// </summary>
        public string Author;

        /// <summary>
        /// Version of the icon pack
        /// </summary>
        public String Version;

        /// <summary>
        /// A list containing all icons of the icon pack
        /// </summary>
        public List<Icon> Icons;

        /// <summary>
        /// True = disable the possibility to edit the icon pack in the icon selector
        /// </summary>
        public bool PackageManagerManaged = false;

        /// <summary>
        /// True = the icon pack is hidden in the icon selector
        /// </summary>
        public bool Hidden = false;

        /// <summary>
        /// Description of the icon pack when exported
        /// </summary>
        public string Description;

        /// <summary>
        /// Link to the used license
        /// </summary>
        public string License = String.Empty;

        /// <summary>
        /// Icon preview
        /// </summary>
        public string IconPreviewBase64 { get; set; }
    }
}
