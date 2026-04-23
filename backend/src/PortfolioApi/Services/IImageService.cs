namespace PortfolioApi.Services;

public interface IImageService
{
    /// Absolute path on disk where converted WebP files are written.
    /// Program.cs uses this to wire the static-file middleware to the same
    /// location, so the configured MediaPath is single-sourced.
    string MediaRoot { get; }

    /// Converts arbitrary image bytes to WebP and stores it under /media.
    /// Returns the public URL path (e.g. "/media/<id>.webp").
    /// When quality is null, the configured default is used.
    Task<string> ConvertToWebpAsync(Stream input, int? quality = null);
}
