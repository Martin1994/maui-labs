using System.Net.Http;
using System.Runtime.InteropServices.JavaScript;

namespace FluentBrowserSample;

internal static partial class SampleDiagnostics
{
    private const string ModuleName = "sample-diagnostics";
    private static readonly CancellationTokenSource Lifetime = new();
    private static JSObject? _module;

    internal const string RepositoryUrl = "https://github.com/Martin1994/maui-labs/tree/fluent-web/platforms/Wasm.FluentUIWebComponents";

    internal static async Task InitializeAsync()
    {
        _module = await JSHost.ImportAsync(ModuleName, "./sample-diagnostics.js");
    }

    internal static async Task<TimeSpan> CaptureStartupAsync()
    {
        // Called during the first MAUI layout. The next browser frame follows a paint
        // opportunity for that layout. The browser's navigation time origin predates Main.
        double milliseconds = await AfterNextFrameAsync();
        Lifetime.Token.ThrowIfCancellationRequested();
        return TimeSpan.FromMilliseconds(milliseconds);
    }

    internal static async Task<long> GetWasmFileSizeAsync()
    {
        Lifetime.Token.ThrowIfCancellationRequested();
        double downloadedBytes = GetDownloadedWasmSize();
        if (double.IsFinite(downloadedBytes) && downloadedBytes > 0 && downloadedBytes == Math.Truncate(downloadedBytes))
        {
            return checked((long)downloadedBytes);
        }

        // Resource Timing can be unavailable. Count the decoded response in C# instead
        // of trusting Content-Length, which may describe gzip/Brotli bytes. This fallback
        // happens after startup timing has finished and uses a small managed read buffer.
        using var client = new HttpClient();
        using var stream = await client.GetStreamAsync(GetWasmUrl(), Lifetime.Token);
        var buffer = new byte[64 * 1024];
        await stream.ReadExactlyAsync(buffer.AsMemory(0, 4), Lifetime.Token);
        if (!buffer.AsSpan(0, 4).SequenceEqual("\0asm"u8))
        {
            throw new InvalidDataException("The Wasm URL did not return a WebAssembly file.");
        }
        long total = 4;
        int count;
        while ((count = await stream.ReadAsync(buffer.AsMemory(), Lifetime.Token)) != 0)
        {
            total = checked(total + count);
        }
        return total;
    }

    internal static void Dispose()
    {
        Lifetime.Cancel();
        _module?.Dispose();
        _module = null;
    }

    [JSImport("afterNextFrame", ModuleName)]
    private static partial Task<double> AfterNextFrameAsync();

    [JSImport("getDownloadedWasmSize", ModuleName)]
    private static partial double GetDownloadedWasmSize();

    [JSImport("getWasmUrl", ModuleName)]
    private static partial string GetWasmUrl();

    [JSImport("openExternal", ModuleName)]
    internal static partial void OpenExternal(string url);
}