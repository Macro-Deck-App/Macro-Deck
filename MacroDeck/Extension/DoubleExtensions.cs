﻿namespace SuchByte.MacroDeck.Extension;

public static class DoubleExtensions
{
    public static double ConvertBytesToMegabytes(this double bytes)
    {
        return bytes / 1024.0f / 1024.0f;
    }
}
