using System.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using YotoLabels.Models;
using YotoLabels.Services;

namespace YotoLabels.Controls;

/// <summary>
/// A full page, drawn at 96 DIPs per inch with its origin at the paper's top-left corner.
/// </summary>
public sealed partial class SheetView : Canvas
{
    private const double Dip = LabelSheet.Dip;

    private readonly SheetProject _project;
    private readonly List<SlotView> _slots = [];
    private readonly bool _interactive;

    private SheetView(SheetProject project, bool interactive)
    {
        _project = project;
        _interactive = interactive;
        Width = project.Sheet.PageWidth * Dip;
        Height = project.Sheet.PageHeight * Dip;
        Background = new SolidColorBrush(Colors.White);
    }

    public IReadOnlyList<SlotView> Slots => _slots;

    /// <summary>The editable on-screen sheet.</summary>
    public static SheetView CreateEditor(SheetProject project, ProjectService files, ImageStore images)
    {
        var view = new SheetView(project, interactive: true);
        foreach (var slot in project.Slots)
        {
            var slotView = new SlotView(slot, project, files, images, interactive: true);
            view._slots.Add(slotView);
            view.Children.Add(slotView);
        }
        view.PositionSlots();
        project.PropertyChanged += view.OnProjectChanged;
        view.Unloaded += (_, _) => project.PropertyChanged -= view.OnProjectChanged;
        return view;
    }

    /// <summary>
    /// A static copy of the sheet for printing. All images must already be loaded in
    /// <paramref name="images"/>, see <see cref="PreloadAsync"/>.
    /// </summary>
    public static SheetView CreateForPrint(SheetProject project, ProjectService files, ImageStore images)
    {
        var view = new SheetView(project, interactive: false);
        foreach (var slot in project.Slots)
        {
            var slotView = new SlotView(slot, project, files, images, interactive: false);
            view._slots.Add(slotView);
            view.Children.Add(slotView);
        }
        view.PositionSlots();
        return view;
    }

    /// <summary>
    /// A calibration page: label outlines, slot numbers and a ruler, no pictures.
    /// Print it on plain paper and hold it against a label sheet.
    /// </summary>
    public static SheetView CreateTestPage(SheetProject project)
    {
        var sheet = project.Sheet;
        var view = new SheetView(project, interactive: false);
        var black = new SolidColorBrush(Colors.Black);
        var grey = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120));
        (double ox, double oy) = OffsetDip(project);

        for (int i = 0; i < sheet.Count; i++)
        {
            var o = sheet.LabelOrigin(i);
            double x = o.X * Dip + ox, y = o.Y * Dip + oy;
            double w = sheet.LabelWidth * Dip, h = sheet.LabelHeight * Dip;

            var outline = new Rectangle { Width = w, Height = h, Stroke = black, StrokeThickness = 0.5 };
            SetLeft(outline, x);
            SetTop(outline, y);
            view.Children.Add(outline);

            // Crosshair in the middle of each label.
            view.Children.Add(new Line { X1 = x + w / 2 - 12, X2 = x + w / 2 + 12, Y1 = y + h / 2, Y2 = y + h / 2, Stroke = grey, StrokeThickness = 0.5 });
            view.Children.Add(new Line { X1 = x + w / 2, X2 = x + w / 2, Y1 = y + h / 2 - 12, Y2 = y + h / 2 + 12, Stroke = grey, StrokeThickness = 0.5 });

            var number = new TextBlock { Text = $"{i + 1}", FontSize = 14, Foreground = grey };
            SetLeft(number, x + 8);
            SetTop(number, y + 6);
            view.Children.Add(number);

            if (sheet.LabelWidth > sheet.CardZoneWidth)
            {
                double cx = x + sheet.CardZoneWidth * Dip;
                view.Children.Add(new Line { X1 = cx, X2 = cx, Y1 = y, Y2 = y + h, Stroke = grey, StrokeThickness = 0.5, StrokeDashArray = [4, 4] });
            }
        }

        // One inch ruler, to check the printer did not scale the page.
        double rx = 1 * Dip, ry = sheet.PageHeight * Dip - 0.3 * Dip;
        if (sheet.MarginTop >= 0.3)
        {
            view.Children.Add(new Line { X1 = rx, X2 = rx + Dip, Y1 = ry, Y2 = ry, Stroke = black, StrokeThickness = 1 });
            view.Children.Add(new Line { X1 = rx, X2 = rx, Y1 = ry - 5, Y2 = ry + 5, Stroke = black, StrokeThickness = 1 });
            view.Children.Add(new Line { X1 = rx + Dip, X2 = rx + Dip, Y1 = ry - 5, Y2 = ry + 5, Stroke = black, StrokeThickness = 1 });
            var caption = new TextBlock
            {
                Text = $"1 inch  ·  {sheet.Name}  ·  offset {project.OffsetXmm:0.#} / {project.OffsetYmm:0.#} mm",
                FontSize = 10,
                Foreground = grey,
            };
            SetLeft(caption, rx + Dip + 10);
            SetTop(caption, ry - 8);
            view.Children.Add(caption);
        }
        return view;
    }

    /// <summary>Loads every picture used by the project so a print copy can be built synchronously.</summary>
    public static Task PreloadAsync(SheetProject project, ProjectService files, ImageStore images) =>
        Task.WhenAll(project.Slots
            .Select(files.ResolveImage)
            .OfType<string>()
            .Distinct()
            .Select(images.GetAsync));

    public void SetSelected(SlotModel? slot)
    {
        foreach (var view in _slots) view.IsSelected = ReferenceEquals(view.Slot, slot);
    }

    private void OnProjectChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SheetProject.OffsetXmm) or nameof(SheetProject.OffsetYmm))
            PositionSlots();
    }

    private void PositionSlots()
    {
        (double ox, double oy) = OffsetDip(_project);
        for (int i = 0; i < _slots.Count; i++)
        {
            var o = _project.Sheet.LabelOrigin(i);
            SetLeft(_slots[i], o.X * Dip + ox);
            SetTop(_slots[i], o.Y * Dip + oy);
        }
    }

    private static (double X, double Y) OffsetDip(SheetProject p) =>
        (p.OffsetXmm / 25.4 * Dip, p.OffsetYmm / 25.4 * Dip);
}
