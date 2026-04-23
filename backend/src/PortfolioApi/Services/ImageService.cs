using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;

namespace PortfolioApi.Services;

public class ImageService : IImageService
{
    private readonly int _defaultQuality;

    public string MediaRoot { get; }

    public ImageService(IOptions<ImageOptions> opt, IWebHostEnvironment env)
    {
        var o = opt.Value;
        MediaRoot = Path.Combine(env.ContentRootPath, o.MediaPath);
        _defaultQuality = o.WebpQuality;
        Directory.CreateDirectory(MediaRoot);
    }

    public async Task<string> ConvertToWebpAsync(Stream input, int? quality = null)
    {
        using var image = await Image.LoadAsync(input);
        var fileName = $"{Guid.NewGuid():N}.webp";
        var path = Path.Combine(MediaRoot, fileName);

        var encoder = new WebpEncoder { Quality = quality ?? _defaultQuality };
        await using var fs = File.Create(path);
        await image.SaveAsync(fs, encoder);

        return $"/media/{fileName}";
    }
}
