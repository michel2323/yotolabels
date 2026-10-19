using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace YotoLabels;

public sealed partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico"));

        if (AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = 960;
            presenter.PreferredMinimumHeight = 720;
        }
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1280, 900));

        AppWindow.Closing += OnClosing;
    }

    public void SetDocumentTitle(string document)
    {
        string title = $"{document} - Yoto Labels";
        Title = title;
        AppTitleBar.Title = title;
    }

    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed) return;

        if (Page.HasUnsavedChanges)
        {
            args.Cancel = true;
            if (!await Page.ConfirmDiscardAsync()) return;
        }

        _closeConfirmed = true;
        Page.Shutdown();
        if (args.Cancel) Close();
    }
}
