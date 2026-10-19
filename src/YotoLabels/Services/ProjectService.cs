using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using YotoLabels.Models;

namespace YotoLabels.Services;

/// <summary>
/// Owns the working folder for the open project and reads/writes .yotolabels files.
/// A .yotolabels file is a zip holding project.json and an images/ folder, so a saved
/// sheet keeps working even if the original pictures are moved or deleted.
/// </summary>
public sealed class ProjectService
{
    public const string FileExtension = ".yotolabels";
    private const string ProjectEntry = "project.json";
    private const string ImagesFolder = "images";
    private const int FormatVersion = 1;

    private static readonly string WorkRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "YotoLabels", "work");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public ProjectService()
    {
        CleanUpStaleWorkFolders();
        WorkFolder = CreateWorkFolder();
    }

    /// <summary>Folder that holds the current project's images.</summary>
    public string WorkFolder { get; private set; }

    /// <summary>Path of the file the project was last opened from or saved to.</summary>
    public string? FilePath { get; private set; }

    public string DisplayName => FilePath is null ? "Untitled" : Path.GetFileNameWithoutExtension(FilePath);

    public string? ResolveImage(SlotModel slot) =>
        slot.ImageFile is null ? null : Path.Combine(WorkFolder, slot.ImageFile.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>Copies a picture into the working folder and returns its relative path.</summary>
    public string ImportImage(string sourcePath)
    {
        string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        string name = $"{Guid.NewGuid():N}{ext}";
        Directory.CreateDirectory(Path.Combine(WorkFolder, ImagesFolder));
        File.Copy(sourcePath, Path.Combine(WorkFolder, ImagesFolder, name));
        return $"{ImagesFolder}/{name}";
    }

    public SheetProject NewProject()
    {
        TryDelete(WorkFolder);
        WorkFolder = CreateWorkFolder();
        FilePath = null;
        return new SheetProject();
    }

    public SheetProject Open(string path)
    {
        string folder = CreateWorkFolder();
        try
        {
            ZipFile.ExtractToDirectory(path, folder);
            string json = File.ReadAllText(Path.Combine(folder, ProjectEntry));
            var dto = JsonSerializer.Deserialize<ProjectDto>(json, JsonOptions)
                      ?? throw new InvalidDataException("The file does not contain a label sheet.");
            if (dto.Format > FormatVersion)
                throw new InvalidDataException("This file was made by a newer version of Yoto Labels.");

            var project = FromDto(dto, folder);
            TryDelete(WorkFolder);
            WorkFolder = folder;
            FilePath = path;
            return project;
        }
        catch
        {
            TryDelete(folder);
            throw;
        }
    }

    public void Save(SheetProject project, string path)
    {
        string temp = path + ".tmp";
        using (var stream = File.Create(temp))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry(ProjectEntry, CompressionLevel.Optimal);
            using (var writer = entry.Open())
                JsonSerializer.Serialize(writer, ToDto(project), JsonOptions);

            foreach (var file in project.Slots.Select(s => s.ImageFile).OfType<string>().Distinct())
            {
                string full = Path.Combine(WorkFolder, file.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(full))
                    zip.CreateEntryFromFile(full, file, CompressionLevel.NoCompression);
            }
        }
        File.Move(temp, path, overwrite: true);
        FilePath = path;
        project.IsDirty = false;
    }

    /// <summary>Removes this session's working folder. Call when the app closes.</summary>
    public void Shutdown() => TryDelete(WorkFolder);

    private static SheetProject FromDto(ProjectDto dto, string folder)
    {
        var project = new SheetProject(LabelSheet.ById(dto.Sheet))
        {
            Layout = dto.Layout,
            RoundedCorners = dto.RoundedCorners,
            PrintCutLine = dto.PrintCutLine,
            TitleFontSize = dto.TitleFontSize > 0 ? dto.TitleFontSize : SheetProject.DefaultTitleFontSize,
            OffsetXmm = dto.OffsetXmm,
            OffsetYmm = dto.OffsetYmm,
        };

        for (int i = 0; i < Math.Min(dto.Slots.Count, project.Slots.Count); i++)
        {
            var src = dto.Slots[i];
            var slot = project.Slots[i];
            slot.ImageFile = IsSafeImagePath(src.Image, folder) ? src.Image : null;
            slot.Zoom = src.Zoom > 0 ? src.Zoom : 1;
            slot.PanX = src.PanX;
            slot.PanY = src.PanY;
            slot.Title = src.Title ?? "";
        }
        project.IsDirty = false;
        return project;
    }

    /// <summary>Only accept images stored inside the project's own images folder.</summary>
    private static bool IsSafeImagePath(string? relative, string folder)
    {
        if (string.IsNullOrEmpty(relative)) return false;
        string full = Path.GetFullPath(Path.Combine(folder, relative));
        string images = Path.GetFullPath(Path.Combine(folder, ImagesFolder)) + Path.DirectorySeparatorChar;
        return full.StartsWith(images, StringComparison.OrdinalIgnoreCase) && File.Exists(full);
    }

    private static ProjectDto ToDto(SheetProject p) => new()
    {
        Format = FormatVersion,
        Sheet = p.Sheet.Id,
        Layout = p.Layout,
        RoundedCorners = p.RoundedCorners,
        PrintCutLine = p.PrintCutLine,
        TitleFontSize = p.TitleFontSize,
        OffsetXmm = p.OffsetXmm,
        OffsetYmm = p.OffsetYmm,
        Slots = p.Slots.Select(s => new SlotDto
        {
            Image = s.ImageFile,
            Zoom = s.Zoom,
            PanX = s.PanX,
            PanY = s.PanY,
            Title = s.Title,
        }).ToList(),
    };

    private static string CreateWorkFolder()
    {
        string folder = Path.Combine(WorkRoot, $"{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Deletes working folders left behind by earlier sessions that are no longer running.</summary>
    private static void CleanUpStaleWorkFolders()
    {
        if (!Directory.Exists(WorkRoot)) return;
        foreach (var dir in Directory.EnumerateDirectories(WorkRoot))
        {
            string name = Path.GetFileName(dir);
            int dash = name.IndexOf('-');
            if (dash > 0 && int.TryParse(name[..dash], out int pid) && IsRunning(pid)) continue;
            TryDelete(dir);
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var p = System.Diagnostics.Process.GetProcessById(pid);
            return !p.HasExited && p.ProcessName.Equals("YotoLabels", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static void TryDelete(string folder)
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch { /* best effort */ }
    }

    private sealed class ProjectDto
    {
        public int Format { get; set; }
        public string? Sheet { get; set; }
        public CardLayout Layout { get; set; }
        public bool RoundedCorners { get; set; } = true;
        public bool PrintCutLine { get; set; } = true;
        public double TitleFontSize { get; set; }
        public double OffsetXmm { get; set; }
        public double OffsetYmm { get; set; }
        public List<SlotDto> Slots { get; set; } = [];
    }

    private sealed class SlotDto
    {
        public string? Image { get; set; }
        public double Zoom { get; set; } = 1;
        public double PanX { get; set; }
        public double PanY { get; set; }
        public string? Title { get; set; }
    }
}
