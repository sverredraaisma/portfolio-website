using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using PortfolioApi.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace PortfolioApi.Tests.Services;

/// Defends the upload path against decompression bombs and SVG/HTML
/// payloads that the byte-level cap can't see.
public sealed class ImageServiceTests : IDisposable
{
    private readonly string _root;
    private readonly ImageService _sut;

    public ImageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "img-svc-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var env = new TestEnv(_root);
        var opt = Options.Create(new ImageOptions { MediaPath = "media", WebpQuality = 80 });
        _sut = new ImageService(opt, env);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task ConvertToWebpAsync_writes_a_webp_file_for_a_well_formed_PNG()
    {
        using var ms = new MemoryStream();
        using (var img = new Image<Rgba32>(width: 16, height: 16))
            await img.SaveAsync(ms, new PngEncoder());
        ms.Position = 0;

        var url = await _sut.ConvertToWebpAsync(ms);

        url.Should().StartWith("/media/").And.EndWith(".webp");
        var fileName = url["/media/".Length..];
        File.Exists(Path.Combine(_sut.MediaRoot, fileName)).Should().BeTrue();
    }

    [Fact]
    public async Task ConvertToWebpAsync_rejects_a_payload_whose_dimensions_exceed_the_cap()
    {
        // Decompression-bomb defence: a 9001x9001 PNG is past MaxFrameSize,
        // so the decoder must refuse the load before allocating the pixel
        // buffer. We surface a user-facing InvalidOperationException so the
        // RPC router maps it to a 400 instead of leaking a 500.
        using var ms = new MemoryStream();
        using (var img = new Image<Rgba32>(width: 9001, height: 9001))
            await img.SaveAsync(ms, new PngEncoder());
        ms.Position = 0;

        var act = async () => await _sut.ConvertToWebpAsync(ms);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*dimensions*");
    }

    [Fact]
    public async Task ConvertToWebpAsync_rejects_a_non_image_payload_with_a_user_facing_error()
    {
        // Whether the bytes are HTML, SVG (which ImageSharp doesn't decode),
        // or random noise, the decoder either says "unknown format" or
        // "invalid content". Both surface as a 400-shaped error.
        var html = System.Text.Encoding.UTF8.GetBytes("<html><body><script>alert(1)</script></body></html>");
        using var ms = new MemoryStream(html);

        var act = async () => await _sut.ConvertToWebpAsync(ms);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*supported format*");
    }

    [Fact]
    public async Task ConvertToWebpAsync_does_not_leak_a_partial_file_when_decoding_fails()
    {
        var html = System.Text.Encoding.UTF8.GetBytes("not an image");
        using var ms = new MemoryStream(html);

        var act = async () => await _sut.ConvertToWebpAsync(ms);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // The .webp file is created inside the using-image block; if the
        // decoder threw before that block ran, no file should exist.
        Directory.GetFiles(_sut.MediaRoot, "*.webp").Should().BeEmpty();
    }

    [Fact]
    public async Task ConvertToWebpAsync_strips_EXIF_so_GPS_and_camera_metadata_dont_leak_to_the_public_url()
    {
        // Source image carries a fake GPS coordinate + camera model that
        // would survive a naive re-encode. The output must have no
        // EXIF profile at all (or no GPS / camera tag) — we publish
        // these images on a public URL.
        using var src = new Image<Rgba32>(width: 32, height: 32);
        var exif = new ExifProfile();
        exif.SetValue(ExifTag.Model, "SECRET-CAM-X1");
        exif.SetValue(ExifTag.GPSLatitudeRef, "N");
        src.Metadata.ExifProfile = exif;

        using var inMs = new MemoryStream();
        await src.SaveAsync(inMs, new JpegEncoder());
        inMs.Position = 0;

        var url = await _sut.ConvertToWebpAsync(inMs);
        var path = Path.Combine(_sut.MediaRoot, url["/media/".Length..]);
        using var produced = await Image.LoadAsync(path);

        // The contract is simply "no EXIF block on the served file" — we
        // null the profile out wholesale, so checking for the cleared
        // reference is the simplest assertion.
        produced.Metadata.ExifProfile.Should().BeNull();
    }

    private sealed class TestEnv(string root) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "PortfolioApi.Tests";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = null!;
    }
}
