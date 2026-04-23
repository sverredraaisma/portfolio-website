using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace PortfolioApi.Services;

public class ImageService
{
    private readonly string _mediaRoot;

    public ImageService(IWebHostEnvironment env)
    {
        _mediaRoot = Path.Combine(env.ContentRootPath, "media");
        Directory.CreateDirectory(_mediaRoot);
    }

    /// Converts arbitrary image bytes to WebP and stores it under /media.
    /// Returns the public URL path (e.g. "/media/<id>.webp").
    public async Task<string> ConvertToWebpAsync(Stream input, int quality = 80)
    {
        using var image = await Image.LoadAsync(input);
        var fileName = $"{Guid.NewGuid():N}.webp";
        var path = Path.Combine(_mediaRoot, fileName);

        var encoder = new WebpEncoder { Quality = quality };
        await using var fs = File.Create(path);
        await image.SaveAsync(fs, encoder);

        return $"/media/{fileName}";
    }
}
