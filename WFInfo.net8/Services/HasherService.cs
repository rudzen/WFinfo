using System.IO;
using System.Security.Cryptography;

namespace WFInfo;

public sealed class HasherService : IHasherService
{
    private static readonly char[] HexUpperAlphabet = "0123456789ABCDEF".ToArray();
    private static readonly char[] HexLowerAlphabet = "0123456789abcdef".ToArray();
    
    public string GetMD5hash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hash = MD5.HashData(stream);
        return ToHex(hash, true);
    }

    private static string ToHex(ReadOnlySpan<byte> source, bool lowerCase = false)
    {
        if (source.Length == 0)
            return string.Empty;

        // excessively avoid stack overflow if source is too large (considering that we're allocating a new string)
        var buffer = source.Length <= 256 
            ? stackalloc char[source.Length * 2]
            : new char[source.Length * 2];
        return ToHexInternal(source, buffer, lowerCase);
    }

    private static string ToHexInternal(ReadOnlySpan<byte> source, Span<char> buffer, bool lowerCase)
    {
        var sourceIndex = 0;
        var alphabet = lowerCase ? HexLowerAlphabet : HexUpperAlphabet;

        for (var i = 0; i < buffer.Length; i += 2)
        {
            var b = source[sourceIndex++];
            buffer[i] = alphabet[b >> 4];
            buffer[i + 1] = alphabet[b & 0xF];
        }

        return new string(buffer);
    }
}