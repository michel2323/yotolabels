using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace YotoLabels.Services;

/// <summary>
/// Decodes images once into ready-to-draw bitmaps. Fully decoded sources matter for
/// printing: a lazily loaded BitmapImage can come out blank on the printed page.
/// Must be used from the UI thread.
/// </summary>
public sealed class ImageStore
{
    /// <summary>Longest side kept in memory. 2 in at 600 dpi is 1200 px, so this is plenty.</summary>
    private const uint MaxPixels = 2000;

    private readonly Dictionary<string, Task<ImageSource?>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public static readonly string[] SupportedExtensions =
        [".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".heic", ".jxr"];

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the decoded image, or null if the file cannot be read.</summary>
    public Task<ImageSource?> GetAsync(string absolutePath)
    {
        if (!_cache.TryGetValue(absolutePath, out var task))
        {
            task = LoadAsync(absolutePath);
            _cache[absolutePath] = task;
        }
        return task;
    }

    /// <summary>Returns the image only if it has already finished loading.</summary>
    public ImageSource? TryGetLoaded(string absolutePath) =>
        _cache.TryGetValue(absolutePath, out var task) && task.IsCompletedSuccessfully ? task.Result : null;

    public void Clear() => _cache.Clear();

    private static async Task<ImageSource?> LoadAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);

            double scale = Math.Min(1.0, MaxPixels / (double)Math.Max(decoder.PixelWidth, decoder.PixelHeight));
            var transform = new BitmapTransform
            {
                ScaledWidth = Math.Max(1u, (uint)Math.Round(decoder.PixelWidth * scale)),
                ScaledHeight = Math.Max(1u, (uint)Math.Round(decoder.PixelHeight * scale)),
                InterpolationMode = BitmapInterpolationMode.Fant,
            };

            using var bitmap = await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            // WriteableBitmap rather than SoftwareBitmapSource: ImageBrush fails fast with the latter.
            var source = new WriteableBitmap(bitmap.PixelWidth, bitmap.PixelHeight);
            bitmap.CopyToBuffer(source.PixelBuffer);
            source.Invalidate();
            return source;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Could not load image {path}: {ex.Message}");
            return null;
        }
    }
}
