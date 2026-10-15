namespace DigitalSorter.AndroidTV;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

        Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("DigitalSorter", (handler, _) =>
        {
            handler.PlatformView.Settings.JavaScriptEnabled = true;
            handler.PlatformView.Settings.DomStorageEnabled = true;
            handler.PlatformView.Settings.AllowFileAccess = false;
            handler.PlatformView.Settings.MixedContentMode = Android.Webkit.MixedContentHandling.NeverAllow;
            handler.PlatformView.AddJavascriptInterface(new SecureStorageBridge(handler.PlatformView), "DigitalSorterNative");
        });

        return builder.Build();
    }
}
