namespace HamsterHub.Mobile;

public sealed class App : Application
{
    public App()
    {
        UserAppTheme = Preferences.Default.Get("theme", "system") switch
        {
            "dark" => AppTheme.Dark, "light" => AppTheme.Light, _ => AppTheme.Unspecified
        };
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(new MainPage());
}
