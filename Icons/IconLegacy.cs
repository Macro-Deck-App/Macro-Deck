using System.Drawing;
using Newtonsoft.Json;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.Icons
{
    public class IconLegacy
    {
        private Image _cachedIconImage;
        private string _iconBase64 = "";
        private string _iconHex128_64Base64 = "";

        public long IconId { get; set; }
        public string IconBase64
        {
            get => _iconBase64;
            set
            {
                _iconBase64 = value;
                try
                {
                    _cachedIconImage = Base64.GetImageFromBase64(value);
                    _iconHex128_64Base64 = Base64.GetBase64ByteArray((Bitmap)IconImage, new Size(128, 64));
                }
                catch { }
            }
        }
        public string IconPack { get; set; }

        [JsonIgnore]
        public Image IconImage
        {
            get
            {
                if (_cachedIconImage != null)
                {
                    return _cachedIconImage;
                }

                return Base64.GetImageFromBase64(_iconBase64);
            }
        }

        [JsonIgnore]
        public string IconHex128_64Base64
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_iconHex128_64Base64))
                {
                    _iconHex128_64Base64 = Base64.GetBase64ByteArray((Bitmap)IconImage, new Size(128, 64));
                }
                return _iconHex128_64Base64;
            }
        }

    }
}
