using Android.Content;
using Android.Content.PM;
using AndroidX.Core.Content;
using AndroidX.Core.Content.PM;
using HamsterHub.Contracts;
using Application = Android.App.Application;

namespace HamsterHub.Mobile;

internal static class AppUpdateInstaller
{
    private static string? pendingPath;
    private static AndroidReleaseDto? pendingRelease;
    private static Context Context => Application.Context;
    public static long InstalledVersion => PackageInfoCompat.GetLongVersionCode(
        Context.PackageManager!.GetPackageInfo(Context.PackageName!, PackageInfoFlags.MetaData)!);

    public static bool NeedsPermission => OperatingSystem.IsAndroidVersionAtLeast(26) &&
        !Context.PackageManager!.CanRequestPackageInstalls();

    public static void Start(string path, AndroidReleaseDto release)
    {
        ValidatePackage(path, release);
        pendingPath = path;
        pendingRelease = release;
        if (OperatingSystem.IsAndroidVersionAtLeast(26) && NeedsPermission)
        {
            Context.StartActivity(new Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
                Android.Net.Uri.Parse("package:" + Context.PackageName)).AddFlags(ActivityFlags.NewTask));
            return;
        }
        Continue();
    }

    public static bool Continue()
    {
        if (pendingPath is null || pendingRelease is null || NeedsPermission) return false;
        var path = pendingPath;
        var release = pendingRelease;
        pendingPath = null;
        pendingRelease = null;
        ValidatePackage(path, release);
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(Context, Context.PackageName + ".updates", new Java.IO.File(path));
        var install = new Intent(Intent.ActionView);
        install.SetDataAndType(uri, "application/vnd.android.package-archive");
        install.AddFlags(ActivityFlags.NewTask | ActivityFlags.GrantReadUriPermission);
        Context.StartActivity(install);
        return true;
    }

    private static void ValidatePackage(string path, AndroidReleaseDto release)
    {
        var manager = Context.PackageManager!;
        var flags = OperatingSystem.IsAndroidVersionAtLeast(28)
            ? PackageInfoFlags.SigningCertificates : PackageInfoFlags.Signatures;
        var installed = manager.GetPackageInfo(Context.PackageName!, flags)!;
        var archive = manager.GetPackageArchiveInfo(path, flags);
        if (archive is null || archive.PackageName != Context.PackageName ||
            PackageInfoCompat.GetLongVersionCode(archive) != release.VersionCode ||
            release.VersionCode <= PackageInfoCompat.GetLongVersionCode(installed))
            throw new InvalidDataException("APK package or version mismatch.");
        var current = Signatures(installed);
        var candidate = Signatures(archive);
        if (current.Length == 0 || !current.SequenceEqual(candidate))
            throw new InvalidDataException("APK signing certificate mismatch.");
    }

#pragma warning disable CS0618 // Android 7 requires the legacy signature API.
    private static string[] Signatures(PackageInfo package)
    {
        var signatures = OperatingSystem.IsAndroidVersionAtLeast(28)
            ? package.SigningInfo?.GetApkContentsSigners() : package.Signatures;
        return signatures?.Select(signature => Convert.ToHexString(signature.ToByteArray()!))
            .Order(StringComparer.Ordinal).ToArray() ?? [];
    }
#pragma warning restore CS0618
}
