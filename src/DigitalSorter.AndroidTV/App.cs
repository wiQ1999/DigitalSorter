namespace DigitalSorter.AndroidTV;

public sealed class App : Application
{
    public App() => UserAppTheme = AppTheme.Dark;
    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(new MainPage()) { Title = "DigitalSorter" };
    }
}
