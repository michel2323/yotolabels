using CommunityToolkit.Mvvm.ComponentModel;

namespace YotoLabels.Models;

/// <summary>
/// One label position on the sheet.
/// </summary>
public sealed class SlotModel(int index) : ObservableObject
{
    public const double MinZoom = 0.2;
    public const double MaxZoom = 8.0;

    private string? _imageFile;
    private double _zoom = 1.0;
    private double _panX;
    private double _panY;
    private string _title = "";

    public int Index { get; } = index;

    /// <summary>Image file path relative to the project working folder, for example "images/abc.png".</summary>
    public string? ImageFile
    {
        get => _imageFile;
        set
        {
            if (SetProperty(ref _imageFile, value)) OnPropertyChanged(nameof(HasImage));
        }
    }

    /// <summary>1 means the image just covers the picture box.</summary>
    public double Zoom
    {
        get => _zoom;
        set => SetProperty(ref _zoom, Math.Clamp(double.IsFinite(value) ? value : 1.0, MinZoom, MaxZoom));
    }

    /// <summary>Horizontal offset as a fraction of the picture box width.</summary>
    public double PanX
    {
        get => _panX;
        set => SetProperty(ref _panX, double.IsFinite(value) ? value : 0);
    }

    /// <summary>Vertical offset as a fraction of the picture box height.</summary>
    public double PanY
    {
        get => _panY;
        set => SetProperty(ref _panY, double.IsFinite(value) ? value : 0);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value ?? "");
    }

    public bool HasImage => !string.IsNullOrEmpty(ImageFile);

    public void ResetPosition()
    {
        Zoom = 1.0;
        PanX = 0;
        PanY = 0;
    }

    public void Clear()
    {
        ImageFile = null;
        Title = "";
        ResetPosition();
    }
}
