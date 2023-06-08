using System.IO;
using System.Security.Cryptography;
using System;

namespace SuchByte.MacroDeck.Extension;

public static class StreamExtensions
{
    public static async ValueTask<string> CalculateSha256Hash(this Stream stream)
    {
        stream.Position = 0;
        
        var bufferedStream = new BufferedStream(stream);
        using var sha256 = SHA256.Create();

        var buffer = new byte[8192];
        int bytesRead;
        while ((bytesRead = await bufferedStream.ReadAsync(buffer)) > 0)
        {
            sha256.TransformBlock(buffer, 0, bytesRead, buffer, 0);
        }
        sha256.TransformFinalBlock(buffer, 0, 0);

        if (sha256.Hash == null)
        {
            throw new InvalidOperationException("Hash was null");
        }
        
        stream.Position = 0;
        return BitConverter.ToString(sha256.Hash).Replace("-", "").ToLower();
    }
}