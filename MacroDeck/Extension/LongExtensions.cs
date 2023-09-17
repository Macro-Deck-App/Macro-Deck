using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SuchByte.MacroDeck.Extension;

public static class LongExtensions
{
    public static double ConvertBytesToMegabytes(this long bytes)
    {
        return bytes / 1024f / 1024f;
    }
}
