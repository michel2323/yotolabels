# Yoto Labels

A small native Windows 11 app for printing your own Yoto card labels on **TownStix US-10** label sheets.

Drop in album covers or any pictures, zoom and position them, add a title, and print ten labels on one Letter sheet. Pick *Microsoft Print to PDF* in the print dialog to get a PDF instead.

Built with WinUI 3 and the Windows App SDK. No browser engine, no account, no subscription.

## Features

- True-to-size preview of the whole sheet.
- Click an empty label or drop image files on it. *Add images* fills the empty labels in order.
- Mouse wheel zooms a picture around the pointer, drag moves it, double-click replaces it.
- Two card layouts:
  - **Square picture + title**: a 2 × 2 in picture with the title beside it. Best for album covers.
  - **Picture fills the card**: the picture covers the whole card with the title along the bottom.
- Rounded corners and a dashed cut line, both optional.
- Printer calibration: shift every label by a fraction of a millimetre, plus a test page to check alignment.
- Save and reopen sheets as `.yotolabels` files. The pictures are stored inside the file.

## The sheet

| | |
|---|---|
| Paper | US Letter, 8.5 × 11 in |
| Labels | 10 per sheet, 4 × 2 in, 2 columns × 5 rows |
| Margins | 0.5 in top and bottom, 0.15 in left and right |
| Gaps | 0.2 in between columns, none between rows |

A Yoto card is about 3.375 × 2.125 in. Each label therefore holds a 3.375 × 2 in card area at its left end. Trim the remaining 0.625 in strip at the dashed line, then stick the label on the card.

## Printing tips

1. Print the **test page** on plain paper first and hold it against a label sheet in front of a window.
2. If the outlines are off, enter the shift under *Printer calibration*. Positive values move right and down.
3. Use Letter paper in the print dialog. The app sends the page at true size, so there is no scaling to adjust.

## Install

Download `YotoLabels-<version>-win-x64.zip` (or `-win-arm64.zip` for ARM PCs such as Snapdragon laptops) from the [Releases](../../releases) page, unzip it anywhere, and run `YotoLabels.exe`. Nothing else needs to be installed.

Windows 10 version 2004 or newer is required. Windows 11 gives the intended look.

You can also open a sheet by passing it on the command line:

```powershell
YotoLabels.exe "C:\path\to\my sheet.yotolabels"
```

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Visual Studio is not required.

```powershell
winget install Microsoft.DotNet.SDK.10
dotnet build src/YotoLabels -c Release
dotnet run --project src/YotoLabels
```

Create the portable folder that the release zip contains:

```powershell
dotnet publish src/YotoLabels -c Release -r win-x64 -p:Platform=x64 -o publish/YotoLabels
```

For ARM64, use `-r win-arm64 -p:Platform=ARM64`. It cross-compiles fine from an x64 machine.

Pushing a tag such as `v0.1.0` runs the release workflow, which publishes the app and attaches the x64 and ARM64 zips to a GitHub release.

## Project layout

| Path | What it holds |
|---|---|
| `src/YotoLabels/Models` | Sheet geometry, label slots and the saved project |
| `src/YotoLabels/Controls` | The label and sheet drawing, shared by the preview and the printout |
| `src/YotoLabels/Services` | Image decoding, saving and loading, printing |
| `extras/yoto-us10-labels.html` | The original browser-only version of the layout, kept as a no-install fallback |

## Troubleshooting

If the app closes unexpectedly, details are written to `%LOCALAPPDATA%\YotoLabels\crash.log`.

## License

MIT. See [LICENSE](LICENSE). Yoto and TownStix are trademarks of their respective owners. This project is not affiliated with either.
