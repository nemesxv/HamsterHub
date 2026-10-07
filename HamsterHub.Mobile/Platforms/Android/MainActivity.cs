using Android.App;
using Android.Content.PM;
using Android.OS;

namespace HamsterHub.Mobile;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnActivityResult(int requestCode, Result resultCode, Android.Content.Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode == ParentAccess.RequestCode) ParentAccess.Complete(resultCode == Result.Ok);
    }
    protected override void OnDestroy()
    {
        ParentAccess.Complete(false);
        base.OnDestroy();
    }
}
