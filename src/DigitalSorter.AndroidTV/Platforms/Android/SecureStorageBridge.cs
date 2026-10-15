using System.Text.Json;
using Android.Webkit;
using Java.Interop;

namespace DigitalSorter.AndroidTV;

public sealed class SecureStorageBridge(Android.Webkit.WebView web) : Java.Lang.Object
{
    [JavascriptInterface]
    [Export("postMessage")]
    public void PostMessage(string payload)
    {
        if (payload.Length > 2048) return;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var page = Application.Current?.Windows.FirstOrDefault()?.Page as MainPage;
            if (page?.BrokerOrigin is null || !Uri.TryCreate(web.Url, UriKind.Absolute, out var current) || current.Scheme != "https" || current.GetLeftPart(UriPartial.Authority) != page.BrokerOrigin) return;
            string? id = null, value = null;
            var failed = false;
            try
            {
                using var json = JsonDocument.Parse(payload);
                id = json.RootElement.GetProperty("id").GetString();
                if (!Guid.TryParse(id, out _)) return;
                switch (json.RootElement.GetProperty("operation").GetString())
                {
                    case "get":
                        try { value = await SecureStorage.Default.GetAsync(page.StorageKey); }
                        catch (Exception) { SecureStorage.Default.Remove(page.StorageKey); }
                        break;
                    case "set":
                        value = json.RootElement.GetProperty("value").GetString();
                        if (value?.Length != 64 || !value.All(char.IsAsciiHexDigit)) return;
                        await SecureStorage.Default.SetAsync(page.StorageKey, value);
                        value = null;
                        break;
                    case "clear": SecureStorage.Default.Remove(page.StorageKey); break;
                    default: return;
                }
            }
            catch (Exception) { failed = true; }
            if (id is not null)
                web.EvaluateJavascript("globalThis.digitalSorterNativeComplete?.(" + JsonSerializer.Serialize(id) + "," + JsonSerializer.Serialize(value) + "," + (failed ? "true" : "false") + ")", null);
        });
    }
}
