using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace YotoLabels.Models;

public enum CardLayout
{
    /// <summary>A 2 x 2 in square picture with the title in the remaining strip. Best for album covers.</summary>
    SquareCoverWithTitle,

    /// <summary>The picture fills the whole card zone, title overlaid at the bottom.</summary>
    FillCard,
}

/// <summary>
/// Everything that is saved in a .yotolabels file.
/// </summary>
public sealed class SheetProject : ObservableObject
{
    public const double DefaultTitleFontSize = 14;

    private CardLayout _layout = CardLayout.SquareCoverWithTitle;
    private bool _roundedCorners = true;
    private bool _printCutLine = true;
    private double _titleFontSize = DefaultTitleFontSize;
    private double _offsetXmm;
    private double _offsetYmm;
    private bool _isDirty;

    public SheetProject() : this(LabelSheet.TownStixUS10) { }

    public SheetProject(LabelSheet sheet)
    {
        Sheet = sheet;
        Slots = new ReadOnlyCollection<SlotModel>(
            Enumerable.Range(0, sheet.Count).Select(i => new SlotModel(i)).ToList());
        foreach (var slot in Slots) slot.PropertyChanged += OnSlotChanged;
    }

    public LabelSheet Sheet { get; }

    public ReadOnlyCollection<SlotModel> Slots { get; }

    public CardLayout Layout
    {
        get => _layout;
        set => SetAndMarkDirty(ref _layout, value);
    }

    public bool RoundedCorners
    {
        get => _roundedCorners;
        set => SetAndMarkDirty(ref _roundedCorners, value);
    }

    public bool PrintCutLine
    {
        get => _printCutLine;
        set => SetAndMarkDirty(ref _printCutLine, value);
    }

    /// <summary>Title size in DIPs (1/96 in).</summary>
    public double TitleFontSize
    {
        get => _titleFontSize;
        set => SetAndMarkDirty(ref _titleFontSize, Math.Clamp(value, 6, 48));
    }

    /// <summary>Printer calibration: shifts every label, in millimetres.</summary>
    public double OffsetXmm
    {
        get => _offsetXmm;
        set => SetAndMarkDirty(ref _offsetXmm, value);
    }

    public double OffsetYmm
    {
        get => _offsetYmm;
        set => SetAndMarkDirty(ref _offsetYmm, value);
    }

    /// <summary>True when there are changes that have not been saved.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        set => SetProperty(ref _isDirty, value);
    }

    public int FilledCount => Slots.Count(s => s.HasImage);

    private void SetAndMarkDirty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (SetProperty(ref field, value, name)) IsDirty = true;
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        IsDirty = true;
        if (e.PropertyName == nameof(SlotModel.HasImage)) OnPropertyChanged(nameof(FilledCount));
    }
}
