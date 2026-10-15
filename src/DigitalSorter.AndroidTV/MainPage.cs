namespace DigitalSorter.AndroidTV;

public sealed class MainPage : ContentPage
{
    private WebView? _web;
    public string? BrokerOrigin { get; private set; }
    public string StorageKey => "device-session-" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(BrokerOrigin ?? "")));

    public MainPage()
    {
        BackgroundColor = Color.FromArgb("#0b1018");
        var saved = Preferences.Default.Get("broker-origin", "");
        if (!string.IsNullOrEmpty(saved)) OpenBroker(saved); else Configure();
    }

    private void Configure()
    {
        var address = new Entry { Placeholder = "https://twoj-serwer.pl", TextColor = Colors.White,
            PlaceholderColor = Color.FromArgb("#8a98ab"), Keyboard = Keyboard.Url, MinimumWidthRequest = 500,
            Text = Preferences.Default.Get("broker-origin", "") };
        var error = new Label { TextColor = Color.FromArgb("#ffb69a") };
        var connect = new Button { Text = "Otwórz DigitalSorter →", BackgroundColor = Color.FromArgb("#7ba7ff"),
            TextColor = Color.FromArgb("#0b1730"), CornerRadius = 12, Padding = new Thickness(24, 15) };
        connect.Clicked += (_, _) =>
        {
            if (!Uri.TryCreate(address.Text?.Trim(), UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
            { error.Text = "Podaj adres HTTPS serwera, bez ścieżki i danych logowania."; return; }
            var origin = uri.GetLeftPart(UriPartial.Authority);
            Preferences.Default.Set("broker-origin", origin); OpenBroker(origin);
        };
        Content = new Grid
        {
            Padding = new Thickness(70),
            Children = { new VerticalStackLayout
            {
                WidthRequest = 600, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center, Spacing = 20,
                Children =
                {
                    new Label { Text = "DigitalSorter", FontSize = 42, FontAttributes = FontAttributes.Bold, TextColor = Color.FromArgb("#54d995") },
                    new Label { Text = "Twoje wspomnienia. Na dużym ekranie.", FontSize = 26, TextColor = Colors.White },
                    new Label { Text = "Wprowadź adres serwera. Następnie połącz konto przez telefon.", FontSize = 19, TextColor = Color.FromArgb("#8a98ab") },
                    address, connect, error
                }
            } }
        };
    }

    private void OpenBroker(string origin)
    {
        BrokerOrigin = origin;
        _web = new WebView { Source = origin, BackgroundColor = BackgroundColor };
        _web.Navigating += (_, e) =>
        {
            if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var target) || target.Scheme != "https" || target.GetLeftPart(UriPartial.Authority) != BrokerOrigin)
                e.Cancel = true;
        };
        _web.Navigated += async (_, e) =>
        {
            if (e.Result != WebNavigationResult.Success)
                await DisplayAlertAsync("Brak połączenia", "Nie można otworzyć serwera. Sprawdź adres, połączenie i certyfikat HTTPS.", "OK");
        };
        Content = _web;
    }

    protected override bool OnBackButtonPressed()
    {
        if (_web is null) return base.OnBackButtonPressed();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var result = await _web.EvaluateJavaScriptAsync("globalThis.digitalSorterBack?.() ?? false");
                if (result == "true") return;
            }
            catch (Exception) { /* Server errors must leave the native menu available. */ }
            var change = await DisplayActionSheetAsync("DigitalSorter", "Wróć", null, "Zmień serwer", "Zamknij aplikację");
            if (change == "Zmień serwer") { _web = null; Configure(); }
            if (change == "Zamknij aplikację") Application.Current?.Quit();
        });
        return true;
    }
}
