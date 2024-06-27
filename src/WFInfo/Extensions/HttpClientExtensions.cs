using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace WFInfo.Extensions;

public static class HttpClientExtensions
{
    public static async Task<HttpResponseMessage> DownloadFile(
        this HttpClient httpClient,
        string url,
        string outputFile)
    {
        var response = await httpClient.GetAsync(url);
        var input = await response.Content.ReadAsStreamAsync();
        await using var fileStream = File.OpenWrite(outputFile);
        await input.CopyToAsync(fileStream);
        return response;
    }

    /// <summary>
    /// Read the content of the result response from the server
    /// </summary>
    /// <param name="response"></param>
    /// <returns></returns>
    public static async Task<string> DecompressContent(this HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Content-Encoding", out var values)
            && !values.Contains("br"))
            return await response.Content.ReadAsStringAsync();
        await using var stream = await response.Content.ReadAsStreamAsync();
        await using Stream decompressed = new BrotliStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(decompressed);
        return await reader.ReadToEndAsync();
    }
    
    public static Stream GetDecompressedStream(this HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Content-Encoding", out var values)
            && !values.Contains("br"))
            return response.Content.ReadAsStream();
        return new BrotliStream(response.Content.ReadAsStream(), CompressionMode.Decompress);
    }
}