using Newtonsoft.Json;
using SQLite;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;

namespace SuchByte.MacroDeck.Icons
{
    public class Icon
    {
        private string _filePath;

        private string _iconBase64 = "";

        public string FilePath
        {
            get => _filePath;
            set
            {
                this._filePath = value;
            }
        }

        public string IconId { get; set; } = Guid.NewGuid().ToString();

        public Image IconImage
        {
            get
            {
                using (var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read))
                {
                    var ms = new MemoryStream();
                    fs.CopyTo(ms);
                    ms.Position = 0;
                    return Image.FromStream(ms);
                }
            }
        }

        public string IconBase64
        {
            get
            {
                if (string.IsNullOrWhiteSpace(this._iconBase64))
                {
                    this._iconBase64 = Utils.Base64.GetBase64FromImage(IconImage);
                }
                return this._iconBase64;
            }
        }

        public string IconHex128_64Base64
        {
            get => Utils.Base64.GetBase64ByteArray((Bitmap)IconImage, new Size(128,64));
        }

    }
}
