﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace SuchByte.MacroDeck.Icons
{
    public class IconPack
    {
        public string PackageId;

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
        public string Version;

        /// <summary>
        /// A list containing all icons of the icon pack
        /// </summary>
        public List<Icon> Icons;
        
        /// <summary>
        /// Icon
        /// </summary>
        public Image IconPackIcon { get; set; }

        public bool ExtensionStoreManaged { get; set; } = false;

        public bool Hidden { get; set; } = false;
    }
}
