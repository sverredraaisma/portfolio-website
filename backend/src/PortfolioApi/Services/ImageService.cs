using Microsoft.Extensions.Options;
using PortfolioApi.Configuration;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Formats.Webp;

namespace PortfolioApi.Services;

public class ImageService : IImageService
{
    // Hard upper bound on a decoded frame. The PostMethods caller already
    // caps the byte-stream at ~6 MiB, but a 1 KiB malicious PNG can decode
    // to billions of pixels (decompression bomb). MaxFrameSize tells the
    // ImageSharp decoder to refuse the load before the pixel buffer is
    // allocated. 8000×8000 covers any realistic portfolio asset and keeps
    // peak memory well under 1 GiB even for 4-byte-per-pixel formats.
    private static readonly Size MaxFrameSize = new(8000, 8000);

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
        // Two-step decode so a decompression-bomb payload is rejected before
        // we allocate the pixel buffer. Image.IdentifyAsync only reads the
        // header, so it's cheap even for hostile inputs. We need a seekable
        // stream so we can rewind for the actual load.
        if (!input.CanSeek)
        {
            var buffered = new MemoryStream();
            await input.CopyToAsync(buffered);
            buffered.Position = 0;
            input = buffered;
        }

        ImageInfo? info;
        try
        {
            info = await Image.IdentifyAsync(input);
        }
        catch (UnknownImageFormatException)
        {
            throw new InvalidOperationException("Image is not a supported format");
        }
        catch (InvalidImageContentException)
        {
            throw new InvalidOperationException("Image content is malformed");
        }
        if (info is null)
            throw new InvalidOperationException("Image is not a supported format");
        if (info.Width > MaxFrameSize.Width || info.Height > MaxFrameSize.Height)
            throw new InvalidOperationException(
                $"Image dimensions exceed the maximum {MaxFrameSize.Width}x{MaxFrameSize.Height}");

        input.Position = 0;
        using var image = await Image.LoadAsync(input);

        // Strip ALL metadata (EXIF, XMP, ICC, IPTC). User-uploaded photos
        // routinely carry GPS coordinates, camera serial, and timestamps —
        // baking those into the served WebP would publish location and
        // device fingerprints with every post image. ImageSharp preserves
        // metadata by default; we have to clear it explicitly.
        image.Metadata.ExifProfile = null;
        image.Metadata.XmpProfile = null;
        image.Metadata.IccProfile = null;
        image.Metadata.IptcProfile = null;

        var fileName = $"{Guid.NewGuid():N}.webp";
        var path = Path.Combine(MediaRoot, fileName);

        var encoder = new WebpEncoder { Quality = quality ?? _defaultQuality };
        await using var fs = File.Create(path);
        await image.SaveAsync(fs, encoder);

        return $"/media/{fileName}";
    }
}
