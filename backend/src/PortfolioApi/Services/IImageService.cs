namespace PortfolioApi.Services;

public interface IImageService
{
    /// Converts arbitrary image bytes to WebP and stores it under /media.
    /// Returns the public URL path (e.g. "/media/<id>.webp").
    Task<string> ConvertToWebpAsync(Stream input, int quality = 80);
}
