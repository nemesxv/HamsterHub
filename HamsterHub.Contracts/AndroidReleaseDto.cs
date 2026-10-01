namespace HamsterHub.Contracts;

public sealed record AndroidReleaseDto(string PackageName, long VersionCode,
    string VersionName, long SizeBytes, string Sha256)
{
    public static bool IsValid(AndroidReleaseDto? release) => release is
        { PackageName: "com.nemesxv.hamsterhub", VersionCode: > 0,
          SizeBytes: > 0 and <= 200 * 1024 * 1024, Sha256.Length: 64 } &&
        !string.IsNullOrWhiteSpace(release.VersionName) && release.VersionName.Length <= 50 &&
        release.Sha256.All(Uri.IsHexDigit);
}
