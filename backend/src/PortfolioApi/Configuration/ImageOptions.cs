namespace PortfolioApi.Configuration;

/// Strongly-typed binding for the "Image" configuration section.
/// MediaPath is resolved against ContentRootPath by ImageService.MediaRoot —
/// Program.cs reads that single source of truth when wiring static files.
public sealed class ImageOptions
{
    public const string Section = "Image";

    public string MediaPath { get; set; } = "media";
    public int WebpQuality { get; set; } = 80;
    public int MaxImageRawBytes { get; set; } = 6 * 1024 * 1024;
    public int MaxImageBase64Bytes { get; set; } = 8 * 1024 * 1024;
}
