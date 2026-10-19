using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.UI;
using YotoLabels.Models;
using YotoLabels.Services;

namespace YotoLabels.Controls;

/// <summary>
/// Draws one label. The same control is used for the on-screen sheet (interactive)
/// and for the printed page (static), so what you see is what prints.
/// Sizes are in DIPs: 96 per inch.
/// </summary>
public sealed partial class SlotView : Grid
{
    private const double Dip = LabelSheet.Dip;
    private const double CornerRadiusDip = 3.0 / 25.4 * Dip; // 3 mm, close to a Yoto card corner

    private readonly SlotModel _slot;
    private readonly SheetProject _project;
    private readonly ProjectService _files;
    private readonly ImageStore _images;
    private readonly bool _interactive;

    private readonly Grid _card = new();
    private readonly Border _cover = new();
    private readonly Border _selection = new();
    private CompositeTransform? _brushTransform;

    private bool _dragging;
    private Windows.Foundation.Point _dragStart;
    private double _panStartX, _panStartY;
    private bool _isSelected;

    public event EventHandler<SlotModel>? ImageRequested;
    public event EventHandler<SlotModel>? SelectRequested;
    public event EventHandler<(SlotModel Slot, IReadOnlyList<string> Paths)>? FilesDropped;

    public SlotView(SlotModel slot, SheetProject project, ProjectService files, ImageStore images, bool interactive)
    {
        _slot = slot;
        _project = project;
        _files = files;
        _images = images;
        _interactive = interactive;

        var sheet = project.Sheet;
        Width = sheet.LabelWidth * Dip;
        Height = sheet.LabelHeight * Dip;

        _card.Width = sheet.CardZoneWidth * Dip;
        _card.Height = sheet.LabelHeight * Dip;
        _card.HorizontalAlignment = HorizontalAlignment.Left;

        if (interactive)
        {
            Background = new SolidColorBrush(Colors.Transparent); // hit-testable for drag and drop
            AllowDrop = true;
            DragOver += OnDragOver;
            Drop += OnDrop;

            _cover.PointerPressed += OnCoverPointerPressed;
            _cover.PointerMoved += OnCoverPointerMoved;
            _cover.PointerReleased += OnCoverPointerReleased;
            _cover.PointerCaptureLost += (_, _) => _dragging = false;
            _cover.PointerWheelChanged += OnCoverWheel;
            _cover.DoubleTapped += (_, e) => { if (_slot.HasImage) { ImageRequested?.Invoke(this, _slot); e.Handled = true; } };
            _card.Tapped += (_, _) => SelectRequested?.Invoke(this, _slot);

            _slot.PropertyChanged += OnModelChanged;
            _project.PropertyChanged += OnModelChanged;
            Unloaded += (_, _) => Detach();
        }

        Rebuild();
    }

    public SlotModel Slot => _slot;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            _selection.BorderThickness = new Thickness(value ? 3 : 0);
        }
    }

    public void Detach()
    {
        _slot.PropertyChanged -= OnModelChanged;
        _project.PropertyChanged -= OnModelChanged;
    }

    private void OnModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SlotModel.Zoom):
            case nameof(SlotModel.PanX):
            case nameof(SlotModel.PanY):
                UpdateBrushTransform();
                break;
            case nameof(SheetProject.IsDirty):
            case nameof(SheetProject.FilledCount):
            case nameof(SheetProject.OffsetXmm):
            case nameof(SheetProject.OffsetYmm):
                break; // handled by the sheet or irrelevant here
            default:
                Rebuild();
                break;
        }
    }

    // ---------------------------------------------------------------- layout

    private void Rebuild()
    {
        Children.Clear();
        _card.Children.Clear();
        _card.ColumnDefinitions.Clear();

        var sheet = _project.Sheet;
        double cardW = sheet.CardZoneWidth * Dip;
        double h = sheet.LabelHeight * Dip;
        double r = _project.RoundedCorners ? CornerRadiusDip : 0;
        bool square = _project.Layout == CardLayout.SquareCoverWithTitle;

        // Screen-only guides: label outline and the strip that gets trimmed off.
        if (_interactive)
        {
            Children.Add(new Rectangle
            {
                Stroke = new SolidColorBrush(Color.FromArgb(255, 190, 190, 190)),
                StrokeThickness = 1,
                StrokeDashArray = [4, 3],
            });
            if (sheet.LabelWidth > sheet.CardZoneWidth)
            {
                Children.Add(new TextBlock
                {
                    Text = "trim",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromArgb(255, 170, 170, 170)),
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(cardW + ((sheet.LabelWidth - sheet.CardZoneWidth) * Dip - 22) / 2, 0, 0, 0),
                });
            }
        }

        // Picture
        _cover.CornerRadius = square ? new CornerRadius(r, 0, 0, r) : new CornerRadius(r);
        _cover.Child = null;
        _cover.Background = null;
        _brushTransform = null;
        if (square)
        {
            _card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(h) });
            _card.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        SetColumn(_cover, 0);
        _card.Children.Add(_cover);

        string? path = _files.ResolveImage(_slot);
        if (path is not null)
        {
            var source = _images.TryGetLoaded(path);
            if (source is not null)
            {
                _brushTransform = new CompositeTransform { CenterX = 0.5, CenterY = 0.5 };
                _cover.Background = new ImageBrush
                {
                    ImageSource = source,
                    Stretch = Stretch.UniformToFill,
                    AlignmentX = AlignmentX.Center,
                    AlignmentY = AlignmentY.Center,
                    RelativeTransform = _brushTransform,
                };
                UpdateBrushTransform();
            }
            else if (_interactive)
            {
                _ = LoadThenRebuildAsync(path);
            }
        }
        else if (_interactive)
        {
            _cover.Background = new SolidColorBrush(Color.FromArgb(255, 234, 239, 252));
            var hint = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 6,
            };
            hint.Children.Add(new FontIcon
            {
                Glyph = "", // Photo
                FontSize = 28,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 96, 112, 160)),
            });
            hint.Children.Add(new TextBlock
            {
                Text = "Click or drop\nan image",
                TextAlignment = TextAlignment.Center,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromArgb(255, 96, 112, 160)),
            });
            _cover.Child = hint;
        }

        // Title
        AddTitle(square, r);

        Children.Add(_card);

        // Cut line between the card zone and the trimmed strip.
        if (sheet.LabelWidth > sheet.CardZoneWidth && (_interactive || _project.PrintCutLine))
        {
            Children.Add(new Line
            {
                X1 = cardW, Y1 = 0, X2 = cardW, Y2 = h,
                Stroke = new SolidColorBrush(Color.FromArgb(255, 150, 150, 150)),
                StrokeThickness = 0.75,
                StrokeDashArray = [4, 4],
                Opacity = _project.PrintCutLine ? 1 : 0.4,
            });
        }

        // Selection outline (screen only).
        if (_interactive)
        {
            _selection.Width = cardW;
            _selection.HorizontalAlignment = HorizontalAlignment.Left;
            _selection.CornerRadius = new CornerRadius(r);
            _selection.BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
            _selection.BorderThickness = new Thickness(_isSelected ? 3 : 0);
            _selection.IsHitTestVisible = false;
            Children.Add(_selection);
        }
    }

    private void AddTitle(bool square, double r)
    {
        string title = _slot.Title ?? "";
        var black = new SolidColorBrush(Colors.Black);

        if (square)
        {
            var panel = new Border
            {
                Background = new SolidColorBrush(Colors.White),
                CornerRadius = new CornerRadius(0, r, r, 0),
                Padding = new Thickness(8),
                Child = new TextBlock
                {
                    Text = title.Length > 0 || !_interactive ? title : "Title",
                    Foreground = title.Length > 0 ? black : new SolidColorBrush(Color.FromArgb(255, 170, 170, 170)),
                    FontSize = _project.TitleFontSize,
                    FontWeight = FontWeights.SemiBold,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.WrapWholeWords,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 6,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            SetColumn(panel, 1);
            _card.Children.Add(panel);
        }
        else if (title.Length > 0)
        {
            _card.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Background = new SolidColorBrush(Color.FromArgb(217, 255, 255, 255)),
                CornerRadius = new CornerRadius(0, 0, r, r),
                Padding = new Thickness(8, 3, 8, 4),
                IsHitTestVisible = false,
                Child = new TextBlock
                {
                    Text = title,
                    Foreground = black,
                    FontSize = _project.TitleFontSize * 0.85,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    MaxLines = 1,
                },
            });
        }
    }

    private async Task LoadThenRebuildAsync(string path)
    {
        await _images.GetAsync(path);
        if (_files.ResolveImage(_slot) == path) Rebuild();
    }

    private void UpdateBrushTransform()
    {
        if (_brushTransform is null) return;
        _brushTransform.ScaleX = _slot.Zoom;
        _brushTransform.ScaleY = _slot.Zoom;
        _brushTransform.TranslateX = _slot.PanX;
        _brushTransform.TranslateY = _slot.PanY;
    }

    // ---------------------------------------------------------------- interaction

    private void OnCoverPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(_cover);
        if (!point.Properties.IsLeftButtonPressed) return;

        SelectRequested?.Invoke(this, _slot);
        if (!_slot.HasImage)
        {
            ImageRequested?.Invoke(this, _slot);
        }
        else
        {
            _dragging = true;
            _dragStart = point.Position;
            _panStartX = _slot.PanX;
            _panStartY = _slot.PanY;
            _cover.CapturePointer(e.Pointer);
        }
        e.Handled = true;
    }

    private void OnCoverPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        var p = e.GetCurrentPoint(_cover).Position;
        double w = Math.Max(1, _cover.ActualWidth), h = Math.Max(1, _cover.ActualHeight);
        _slot.PanX = _panStartX + (p.X - _dragStart.X) / w;
        _slot.PanY = _panStartY + (p.Y - _dragStart.Y) / h;
        e.Handled = true;
    }

    private void OnCoverPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        _cover.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    /// <summary>Zooms around the mouse pointer so the spot under it stays put.</summary>
    private void OnCoverWheel(object sender, PointerRoutedEventArgs e)
    {
        if (!_slot.HasImage) return;
        var point = e.GetCurrentPoint(_cover);
        double w = Math.Max(1, _cover.ActualWidth), h = Math.Max(1, _cover.ActualHeight);
        double ux = point.Position.X / w, uy = point.Position.Y / h;

        double oldZoom = _slot.Zoom;
        double newZoom = Math.Clamp(oldZoom * Math.Pow(1.1, point.Properties.MouseWheelDelta / 120.0),
                                    SlotModel.MinZoom, SlotModel.MaxZoom);
        double k = newZoom / oldZoom;
        _slot.PanX = (ux - 0.5) - (ux - 0.5 - _slot.PanX) * k;
        _slot.PanY = (uy - 0.5) - (uy - 0.5 - _slot.PanY) * k;
        _slot.Zoom = newZoom;
        SelectRequested?.Invoke(this, _slot);
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.DragUIOverride.Caption = "Add to this label";
        }
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            var paths = items.OfType<StorageFile>()
                             .Select(f => f.Path)
                             .Where(p => !string.IsNullOrEmpty(p) && ImageStore.IsSupported(p))
                             .ToList();
            if (paths.Count > 0) FilesDropped?.Invoke(this, (_slot, paths));
        }
        finally
        {
            deferral.Complete();
        }
    }
}
