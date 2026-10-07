using Android.App;
using Android.Content;

namespace HamsterHub.Mobile;

// Android owns the PIN/password. HamsterHub never stores or implements a device credential.
internal static class ParentAccess
{
    internal const int RequestCode = 7409;
    private static TaskCompletionSource<bool>? completion;
    public static bool IsAuthenticating => completion is not null;
    public static async Task<bool> ConfirmAsync()
    {
        if (completion is not null) return false;
        if (Platform.CurrentActivity is not MainActivity activity) return false;
        var keyguard = (KeyguardManager?)activity.GetSystemService(Context.KeyguardService);
        if (keyguard?.IsDeviceSecure != true) return false;
        using var intent = keyguard.CreateConfirmDeviceCredentialIntent(Strings.Get("ParentUnlock"), Strings.Get("ParentUnlockHint"));
        if (intent is null) return false;
        var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        completion = pending;
        try
        {
            activity.StartActivityForResult(intent, RequestCode);
            return await pending.Task;
        }
        finally { completion = null; }
    }
    internal static void Complete(bool success) => completion?.TrySetResult(success);
}
