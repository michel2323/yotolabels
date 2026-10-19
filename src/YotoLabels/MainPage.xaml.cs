using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using Windows.Graphics.Printing;
using YotoLabels.Controls;
using YotoLabels.Models;
using YotoLabels.Services;

namespace YotoLabels;

public sealed partial class MainPage : Page
{
    private const double PointsPerDip = 0.75;

    private readonly ProjectService _files = new();
    private readonly ImageStore _images = new();
    private PrintService? _print;

    private SheetProject _project;
    private SheetView? _sheet;
    private SlotModel? _selected;
    private bool _syncing;
    private bool _busy;

    public MainPage()
    {
        InitializeComponent();
        _project = new SheetProject();
        Loaded += OnLoaded;
    }

    public bool HasUnsavedChanges => _project.IsDirty;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            _print = new PrintService(App.WindowHandle);
        }
        catch (Exception ex)
        {
            PrintButton.IsEnabled = false;
            ShowError("Printing is not available on this PC.", ex);
        }

        LoadProject(_project);

        if (App.StartupFile is { } file && File.Exists(file))
            await OpenFileAsync(file);
    }

    // ------------------------------------------------------------------ project

    private void LoadProject(SheetProject project)
    {
        _project.PropertyChanged -= OnProjectChanged;
        Select(null);

        _project = project;
        _images.Clear();

        _sheet = SheetView.CreateEditor(project, _files, _images);
        foreach (var slotView in _sheet.Slots)
        {
            slotView.ImageRequested += async (_, slot) => await PickImageForSlotAsync(slot);
            slotView.SelectRequested += (_, slot) => Select(slot);
            slotView.FilesDropped += (_, e) => AssignFiles(e.Slot, e.Paths);
        }
        PageFrame.Child = _sheet;

        project.PropertyChanged += OnProjectChanged;
        SyncSheetControls();
        UpdateWindowTitle();
    }

    private void OnProjectChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SheetProject.IsDirty)) UpdateWindowTitle();
    }

    private void UpdateWindowTitle() =>
        App.MainWindow?.SetDocumentTitle((_project.IsDirty ? "*" : "") + _files.DisplayName);

    /// <summary>Asks to save unsaved changes. Returns false if the user cancelled.</summary>
    public async Task<bool> ConfirmDiscardAsync()
    {
        if (!_project.IsDirty) return true;

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Save changes?",
            Content = $"Do you want to save the changes to {_files.DisplayName}?",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Don't save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        };
        return await dialog.ShowAsync() switch
        {
            ContentDialogResult.Primary => await SaveAsync(saveAs: false),
            ContentDialogResult.Secondary => true,
            _ => false,
        };
    }

    public void Shutdown() => _files.Shutdown();

    private async void OnNew(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;
        LoadProject(_files.NewProject());
    }

    private async void OnOpen(object sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscardAsync()) return;

        var picker = new FileOpenPicker(App.MainWindow!.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            CommitButtonText = "Open",
        };
        picker.FileTypeFilter.Add(ProjectService.FileExtension);
        var result = await picker.PickSingleFileAsync();
        if (result is not null) await OpenFileAsync(result.Path);
    }

    private async Task OpenFileAsync(string path)
    {
        try
        {
            LoadProject(_files.Open(path));
            await SheetView.PreloadAsync(_project, _files, _images);
        }
        catch (Exception ex)
        {
            ShowError($"Could not open {Path.GetFileName(path)}.", ex);
        }
    }

    private async void OnSave(object sender, RoutedEventArgs e) => await SaveAsync(saveAs: false);

    private async void OnSaveAs(object sender, RoutedEventArgs e) => await SaveAsync(saveAs: true);

    private async Task<bool> SaveAsync(bool saveAs)
    {
        string? path = saveAs ? null : _files.FilePath;
        if (path is null)
        {
            var picker = new FileSavePicker(App.MainWindow!.AppWindow.Id)
            {
                SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
                SuggestedFileName = _files.FilePath is null ? "Yoto labels" : _files.DisplayName,
                DefaultFileExtension = ProjectService.FileExtension,
            };
            picker.FileTypeChoices.Add("Yoto label sheet", [ProjectService.FileExtension]);
            var result = await picker.PickSaveFileAsync();
            if (result is null) return false;
            path = result.Path;
            if (!path.EndsWith(ProjectService.FileExtension, StringComparison.OrdinalIgnoreCase))
                path += ProjectService.FileExtension;
        }

        try
        {
            _files.Save(_project, path);
            UpdateWindowTitle();
            return true;
        }
        catch (Exception ex)
        {
            ShowError($"Could not save {Path.GetFileName(path)}.", ex);
            return false;
        }
    }

    // ------------------------------------------------------------------ images

    private async void OnAddImages(object sender, RoutedEventArgs e)
    {
        var paths = await PickImagesAsync(multiple: true);
        if (paths.Count == 0) return;

        var empty = _project.Slots.Where(s => !s.HasImage).ToList();
        if (empty.Count == 0)
        {
            ShowError("All ten labels already have a picture. Remove one or start a new sheet.");
            return;
        }

        int count = Math.Min(paths.Count, empty.Count);
        for (int i = 0; i < count; i++) SetImage(empty[i], paths[i]);
        Select(empty[0]);

        if (paths.Count > empty.Count)
            ShowError($"Only {empty.Count} empty labels were left, so {paths.Count - empty.Count} pictures were not added.");
    }

    private async Task PickImageForSlotAsync(SlotModel slot)
    {
        var paths = await PickImagesAsync(multiple: false);
        if (paths.Count == 0) return;
        SetImage(slot, paths[0]);
        Select(slot);
    }

    private async Task<IReadOnlyList<string>> PickImagesAsync(bool multiple)
    {
        var picker = new FileOpenPicker(App.MainWindow!.AppWindow.Id)
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail,
        };
        foreach (var ext in ImageStore.SupportedExtensions) picker.FileTypeFilter.Add(ext);

        if (multiple)
        {
            var results = await picker.PickMultipleFilesAsync();
            return results?.Select(r => r.Path).ToList() ?? [];
        }
        var single = await picker.PickSingleFileAsync();
        return single is null ? [] : [single.Path];
    }

    /// <summary>Dropping several files fills this label and the ones after it.</summary>
    private void AssignFiles(SlotModel start, IReadOnlyList<string> paths)
    {
        for (int i = 0; i < paths.Count && start.Index + i < _project.Slots.Count; i++)
            SetImage(_project.Slots[start.Index + i], paths[i]);
        Select(start);
    }

    private void SetImage(SlotModel slot, string sourcePath)
    {
        try
        {
            slot.ImageFile = _files.ImportImage(sourcePath);
            slot.ResetPosition();
        }
        catch (Exception ex)
        {
            ShowError($"Could not add {Path.GetFileName(sourcePath)}.", ex);
        }
    }

    private async void OnClearAll(object sender, RoutedEventArgs e)
    {
        if (_project.Slots.All(s => !s.HasImage && string.IsNullOrEmpty(s.Title))) return;
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Clear all labels?",
            Content = "This removes every picture and title from the sheet.",
            PrimaryButtonText = "Clear all",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        foreach (var slot in _project.Slots) slot.Clear();
        SyncSelected();
    }

    // ------------------------------------------------------------------ printing

    private async void OnPrint(object sender, RoutedEventArgs e)
    {
        if (_print is null || _busy) return;
        _busy = true;
        try
        {
            await SheetView.PreloadAsync(_project, _files, _images);
            var page = SheetView.CreateForPrint(_project, _files, _images);
            var outcome = await _print.PrintAsync($"Yoto labels - {_files.DisplayName}", page);
            if (outcome == PrintTaskCompletion.Failed) ShowError("The printer reported an error.");
        }
        catch (Exception ex)
        {
            ShowError("Could not print.", ex);
        }
        finally
        {
            _busy = false;
        }
    }

    private async void OnPrintTestPage(object sender, RoutedEventArgs e)
    {
        if (_print is null || _busy) return;
        _busy = true;
        try
        {
            var outcome = await _print.PrintAsync("Yoto labels - test page", SheetView.CreateTestPage(_project));
            if (outcome == PrintTaskCompletion.Failed) ShowError("The printer reported an error.");
        }
        catch (Exception ex)
        {
            ShowError("Could not print the test page.", ex);
        }
        finally
        {
            _busy = false;
        }
    }

    // ------------------------------------------------------------------ inspector: sheet

    private void SyncSheetControls()
    {
        _syncing = true;
        SheetName.Text = _project.Sheet.Name;
        LayoutChoice.SelectedIndex = _project.Layout == CardLayout.SquareCoverWithTitle ? 0 : 1;
        TitleSizeBox.Value = Math.Round(_project.TitleFontSize * PointsPerDip, 1);
        RoundedToggle.IsOn = _project.RoundedCorners;
        CutLineToggle.IsOn = _project.PrintCutLine;
        OffsetXBox.Value = _project.OffsetXmm;
        OffsetYBox.Value = _project.OffsetYmm;
        _syncing = false;
    }

    private void OnLayoutChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || LayoutChoice.SelectedIndex < 0) return;
        _project.Layout = LayoutChoice.SelectedIndex == 0 ? CardLayout.SquareCoverWithTitle : CardLayout.FillCard;
    }

    private void OnTitleSizeChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_syncing) return;
        if (double.IsNaN(e.NewValue)) { sender.Value = Math.Round(_project.TitleFontSize * PointsPerDip, 1); return; }
        _project.TitleFontSize = e.NewValue / PointsPerDip;
    }

    private void OnRoundedToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncing) _project.RoundedCorners = RoundedToggle.IsOn;
    }

    private void OnCutLineToggled(object sender, RoutedEventArgs e)
    {
        if (!_syncing) _project.PrintCutLine = CutLineToggle.IsOn;
    }

    private void OnOffsetChanged(NumberBox sender, NumberBoxValueChangedEventArgs e)
    {
        if (_syncing) return;
        if (double.IsNaN(e.NewValue)) { sender.Value = 0; return; }
        if (sender == OffsetXBox) _project.OffsetXmm = e.NewValue;
        else _project.OffsetYmm = e.NewValue;
    }

    // ------------------------------------------------------------------ inspector: selected label

    private void Select(SlotModel? slot)
    {
        if (_selected is not null) _selected.PropertyChanged -= OnSelectedChanged;
        _selected = slot;
        if (_selected is not null) _selected.PropertyChanged += OnSelectedChanged;
        _sheet?.SetSelected(slot);
        SyncSelected();
    }

    private void OnSelectedChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SlotModel.PanX) or nameof(SlotModel.PanY)) return;
        SyncSelected();
    }

    private void SyncSelected()
    {
        _syncing = true;
        var slot = _selected;
        SelectedPanel.Visibility = slot is null ? Visibility.Collapsed : Visibility.Visible;
        SelectedHint.Visibility = slot is null || !slot.HasImage ? Visibility.Visible : Visibility.Collapsed;
        if (slot is not null)
        {
            SelectedHeader.Text = $"Label {slot.Index + 1}";
            if (TitleBox.Text != slot.Title) TitleBox.Text = slot.Title;
            ZoomSlider.Value = Math.Round(slot.Zoom * 100);
            ZoomSlider.IsEnabled = slot.HasImage;
        }
        else
        {
            SelectedHeader.Text = "No label selected";
        }
        _syncing = false;
    }

    private void OnTitleChanged(object sender, TextChangedEventArgs e)
    {
        if (!_syncing && _selected is not null) _selected.Title = TitleBox.Text;
    }

    private void OnZoomSliderChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (!_syncing && _selected is { HasImage: true }) _selected.Zoom = e.NewValue / 100.0;
    }

    private async void OnReplaceImage(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) await PickImageForSlotAsync(_selected);
    }

    private void OnResetPosition(object sender, RoutedEventArgs e) => _selected?.ResetPosition();

    private void OnRemoveImage(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        _selected.ImageFile = null;
        _selected.ResetPosition();
    }

    // ------------------------------------------------------------------ errors

    private void ShowError(string message, Exception? ex = null)
    {
        ErrorBar.Message = ex is null ? message : $"{message} {ex.Message}";
        ErrorBar.IsOpen = true;
    }
}
