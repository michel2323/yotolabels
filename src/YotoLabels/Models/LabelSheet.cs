using Windows.Foundation;

namespace YotoLabels.Models;

/// <summary>
/// Physical description of a label sheet. All lengths are in inches.
/// </summary>
public sealed record LabelSheet(
    string Id,
    string Name,
    double PageWidth,
    double PageHeight,
    double LabelWidth,
    double LabelHeight,
    int Columns,
    int Rows,
    double MarginTop,
    double MarginLeft,
    double ColumnGap,
    double RowGap)
{
    /// <summary>XAML device independent pixels per inch.</summary>
    public const double Dip = 96.0;

    /// <summary>Yoto card long side. The card zone sits at the left of each label.</summary>
    public const double CardWidth = 3.375;

    /// <summary>
    /// TownStix US-10: Letter sheet, ten 4 x 2 in labels in 2 columns and 5 rows,
    /// 0.5 in top/bottom margins, 0.15 in side margins, 0.2 in column gap, no row gap.
    /// </summary>
    public static LabelSheet TownStixUS10 { get; } = new(
        Id: "townstix-us10",
        Name: "TownStix US-10 (4 × 2 in, 10 per sheet)",
        PageWidth: 8.5,
        PageHeight: 11,
        LabelWidth: 4,
        LabelHeight: 2,
        Columns: 2,
        Rows: 5,
        MarginTop: 0.5,
        MarginLeft: 0.15,
        ColumnGap: 0.2,
        RowGap: 0);

    public static IReadOnlyList<LabelSheet> All { get; } = [TownStixUS10];

    public static LabelSheet ById(string? id) =>
        All.FirstOrDefault(s => s.Id == id) ?? TownStixUS10;

    public int Count => Columns * Rows;

    /// <summary>Card zone width, never wider than the label.</summary>
    public double CardZoneWidth => Math.Min(CardWidth, LabelWidth);

    /// <summary>Top-left corner of a label in inches, slots are numbered row by row.</summary>
    public Point LabelOrigin(int index)
    {
        int row = index / Columns;
        int col = index % Columns;
        return new Point(
            MarginLeft + col * (LabelWidth + ColumnGap),
            MarginTop + row * (LabelHeight + RowGap));
    }
}
