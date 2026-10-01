using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using HamsterHub.Client;
using HamsterHub.Contracts;
using HamsterHub.Controllers;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;

namespace HamsterHub.Tests.Controllers;

public sealed class AppUpdateTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "HamsterHub-update-tests", Guid.NewGuid().ToString("N"));
    private static readonly byte[] Apk = [1, 2, 3, 4];
    private static AndroidReleaseDto Release => new("com.nemesxv.hamsterhub", 16, "0.1.16", Apk.Length,
        Convert.ToHexString(SHA256.HashData(Apk)));

    [Fact]
    public async Task Version_OnlyAdvertisesMatchingPublishedApk()
    {
        var directory = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(directory);
        var controller = new AppDownloadController(new EnvironmentStub(root))
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.IsType<NotFoundResult>(await controller.Version(TestContext.Current.CancellationToken));
        await File.WriteAllBytesAsync(Path.Combine(directory, "HamsterHub.apk"), Apk, TestContext.Current.CancellationToken);
        var metadata = Path.Combine(directory, "HamsterHub.version.json");
        await File.WriteAllTextAsync(metadata, JsonSerializer.Serialize(Release), TestContext.Current.CancellationToken);
        Assert.Equal(Release, Assert.IsType<OkObjectResult>(await controller.Version(TestContext.Current.CancellationToken)).Value);
        await File.WriteAllBytesAsync(Path.Combine(directory, "HamsterHub.apk"), [4, 3, 2, 1], TestContext.Current.CancellationToken);
        Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Version(TestContext.Current.CancellationToken)).StatusCode);
        await File.WriteAllTextAsync(metadata, "{}", TestContext.Current.CancellationToken);
        Assert.IsType<NotFoundResult>(await controller.Version(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Download_VerifiesArtifactAndReusesVerifiedCache()
    {
        var requests = 0;
        using var http = new HttpClient(new Handler(request =>
        {
            requests++;
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("/download/android", request.RequestUri!.AbsolutePath);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Apk) };
        })) { BaseAddress = new Uri("https://family.example/") };
        var client = new AndroidUpdateClient(http);
        var path = Path.Combine(root, "updates", "HamsterHub.apk");
        await client.DownloadAsync(Release, path, cancellationToken: TestContext.Current.CancellationToken);
        await client.DownloadAsync(Release, path, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(1, requests);
        Assert.Equal(Apk, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Download_RejectsCorruptOrTruncatedArtifactWithoutReplacingCache(bool truncated)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "HamsterHub.apk");
        await File.WriteAllBytesAsync(path, [9], TestContext.Current.CancellationToken);
        using var http = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(truncated ? [1, 2] : [4, 3, 2, 1]) }))
            { BaseAddress = new Uri("https://family.example/") };
        await Assert.ThrowsAsync<InvalidDataException>(() => new AndroidUpdateClient(http).DownloadAsync(Release, path, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(path + ".part"));
    }

    [Fact]
    public async Task Check_OldServerIsCompatibleAndWrongPackageIsRejected()
    {
        using var absent = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)))
            { BaseAddress = new Uri("https://family.example/") };
        Assert.Null(await new AndroidUpdateClient(absent).CheckAsync(TestContext.Current.CancellationToken));
        using var invalid = new HttpClient(new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = JsonContent.Create(Release with { PackageName = "other.app" }) }))
            { BaseAddress = new Uri("https://family.example/") };
        await Assert.ThrowsAsync<InvalidDataException>(() => new AndroidUpdateClient(invalid).CheckAsync(TestContext.Current.CancellationToken));
    }

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(respond(request));
    }

    private sealed class EnvironmentStub(string root) : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "HamsterHub";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}

