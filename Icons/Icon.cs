using Newtonsoft.Json;
using SQLite;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;

namespace SuchByte.MacroDeck.Icons
{
    public class Icon
    {
        private Image _cachedIconImage;
        private string _iconBase64 = "";

        public long IconId { get; set; }
        public string IconBase64 { 
            get
            {
                return this._iconBase64;
            }
            set {
                this._iconBase64 = value;
                if (MacroDeck.Configuration.CacheIcons)
                {
                    try
                    {
                        this._cachedIconImage = Utils.Base64.GetImageFromBase64(value);
                    } catch { }
                }
            }
        }
        public string IconPack { get; set; }

        [JsonIgnore]
        public Image IconImage { 
            get
            {
                if (this._cachedIconImage != null)
                {
                    return this._cachedIconImage;
                } else
                {
                    return Utils.Base64.GetImageFromBase64(this._iconBase64);
                }
            }
        }

    }
}
