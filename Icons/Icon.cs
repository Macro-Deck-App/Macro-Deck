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
        private string _filePath;

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
            get => (Image)Image.FromFile(_filePath).Clone();
        }

        public string IconBase64
        {
            get => Utils.Base64.GetBase64FromImage(IconImage);
        }

        public string IconHex128_64Base64
        {
            get => Utils.Base64.GetBase64ByteArray((Bitmap)IconImage, new Size(128,64));
        }

    }
}
