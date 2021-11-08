using System;
using System.Collections.Generic;
using System.Text;

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
    }
}
