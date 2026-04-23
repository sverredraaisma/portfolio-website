using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace PortfolioApi.Services;

public class ImageService : IImageService
{
    private readonly string _mediaRoot;

    public ImageService(IWebHostEnvironment env)
    {
        _mediaRoot = Path.Combine(env.ContentRootPath, "media");
        Directory.CreateDirectory(_mediaRoot);
    }

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
