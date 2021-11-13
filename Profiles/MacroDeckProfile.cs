using SuchByte.MacroDeck.Folders;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace SuchByte.MacroDeck.Profiles
{
    public class MacroDeckProfile : IDisposable
    {
        private IntPtr _bufferPtr;
        public int BUFFER_SIZE = 1024 * 1024; // 1 MB
        private bool _disposed = false;

        public MacroDeckProfile()
        {
            _bufferPtr = Marshal.AllocHGlobal(BUFFER_SIZE);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                // Free any other managed objects here.
            }

            foreach (MacroDeckFolder folder in this.Folders)
            {
                folder.Dispose();
            }

            // Free any unmanaged objects here.
            Marshal.FreeHGlobal(_bufferPtr);
            _disposed = true;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~MacroDeckProfile()
        {
            Dispose(false);
        }

        public string ProfileId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public List<Folders.MacroDeckFolder> Folders { get; set; } = new List<Folders.MacroDeckFolder>();
        public int Rows { get; set; } = 3;
        public int Columns { get; set; } = 5;
        public int ButtonSpacing { get; set; } = 10;
        public int ButtonRadius { get; set; } = 40;
        public bool ButtonBackground { get; set; } = true;


    }
}
