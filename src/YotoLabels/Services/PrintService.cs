using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Printing;
using Windows.Graphics.Printing;
using Windows.Graphics.Printing.OptionDetails;

namespace YotoLabels.Services;

/// <summary>
/// Prints a single page through the Windows print dialog. The page element is placed with
/// its origin at the top-left corner of the paper, so nothing is scaled or re-centred.
/// Choosing "Microsoft Print to PDF" in the dialog saves a PDF.
/// </summary>
public sealed class PrintService
{
    private readonly IntPtr _hwnd;
    private readonly PrintManager _printManager;

    private PrintDocument? _document;
    private IPrintDocumentSource? _documentSource;
    private FrameworkElement? _page;
    private string _jobName = "Yoto labels";
    private TaskCompletionSource<PrintTaskCompletion>? _completion;

    public PrintService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _printManager = PrintManagerInterop.GetForWindow(hwnd);
        _printManager.PrintTaskRequested += OnPrintTaskRequested;
    }

    public static bool IsSupported => PrintManager.IsSupported();

    /// <summary>
    /// Shows the print dialog for one page. Returns the outcome, or null if the dialog was closed
    /// without printing.
    /// </summary>
    public async Task<PrintTaskCompletion?> PrintAsync(string jobName, FrameworkElement page)
    {
        if (_completion is not null) return null; // a print is already in progress

        _jobName = jobName;
        _page = page;

        _document = new PrintDocument();
        _documentSource = _document.DocumentSource; // must be read on the UI thread
        _document.Paginate += OnPaginate;
        _document.GetPreviewPage += OnGetPreviewPage;
        _document.AddPages += OnAddPages;

        _completion = new TaskCompletionSource<PrintTaskCompletion>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            bool shown = await PrintManagerInterop.ShowPrintUIForWindowAsync(_hwnd);
            if (!shown) return null;
            return await _completion.Task;
        }
        finally
        {
            _document.Paginate -= OnPaginate;
            _document.GetPreviewPage -= OnGetPreviewPage;
            _document.AddPages -= OnAddPages;
            _document = null;
            _documentSource = null;
            _page = null;
            _completion = null;
        }
    }

    private void OnPrintTaskRequested(PrintManager sender, PrintTaskRequestedEventArgs args)
    {
        var source = _documentSource;
        if (source is null) return;

        var task = args.Request.CreatePrintTask(_jobName, request => request.SetSource(source));

        // Labels must print at true size on Letter paper.
        task.Options.MediaSize = PrintMediaSize.NorthAmericaLetter;
        task.Options.Orientation = PrintOrientation.Portrait;
        task.Options.PrintQuality = PrintQuality.High;
        task.Options.ColorMode = PrintColorMode.Color; // some printers default to grayscale

        var details = PrintTaskOptionDetails.GetFromPrintTaskOptions(task.Options);
        details.DisplayedOptions.Clear();
        details.DisplayedOptions.Add(StandardPrintTaskOptions.Copies);
        details.DisplayedOptions.Add(StandardPrintTaskOptions.MediaSize);
        details.DisplayedOptions.Add(StandardPrintTaskOptions.MediaType);
        details.DisplayedOptions.Add(StandardPrintTaskOptions.PrintQuality);
        details.DisplayedOptions.Add(StandardPrintTaskOptions.ColorMode);

        task.Completed += (_, e) => _completion?.TrySetResult(e.Completion);
    }

    private void OnPaginate(object sender, PaginateEventArgs e)
    {
        _document?.SetPreviewPageCount(1, PreviewPageCountType.Final);
    }

    private void OnGetPreviewPage(object sender, GetPreviewPageEventArgs e)
    {
        if (_page is not null) _document?.SetPreviewPage(e.PageNumber, _page);
    }

    private void OnAddPages(object sender, AddPagesEventArgs e)
    {
        if (_document is null || _page is null) return;
        _document.AddPage(_page);
        _document.AddPagesComplete();
    }
}
