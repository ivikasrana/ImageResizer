using System.IO;
using System.Text.RegularExpressions;

namespace ImageResizer;

sealed record ResizeTask(string Source, string Target);

static partial class BatchPlan
{
    static readonly HashSet<string> Extensions =
        new([".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp"], StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"^New_\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}$")]
    private static partial Regex OutputFolderName();

    public static string NewOutputFolder(string destDir) =>
        Path.Combine(destDir, $"New_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}");

    public static List<ResizeTask> Create(string sourceDir, string destDir, string outRoot, string extension, bool recursive)
    {
        var excluded = PreviousOutputFolders(destDir).Append(outRoot)
            .Select(d => Path.TrimEndingDirectorySeparator(d) + Path.DirectorySeparatorChar)
            .ToList();

        var files = Directory.EnumerateFiles(sourceDir, "*", new EnumerationOptions
            {
                RecurseSubdirectories = recursive,
                IgnoreInaccessible = true,
                AttributesToSkip = 0,
            })
            .Where(f => Extensions.Contains(Path.GetExtension(f))
                        && !excluded.Any(d => f.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
            .Order(StringComparer.OrdinalIgnoreCase);

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var tasks = new List<ResizeTask>();
        foreach (var file in files)
        {
            var stem = Path.ChangeExtension(Path.Combine(outRoot, Path.GetRelativePath(sourceDir, file)), null);
            var target = stem + extension;
            for (var n = 2; !used.Add(target); n++) target = $"{stem} ({n}){extension}";
            tasks.Add(new ResizeTask(file, target));
        }
        return tasks;
    }

    static IEnumerable<string> PreviousOutputFolders(string destDir) =>
        Directory.Exists(destDir)
            ? Directory.EnumerateDirectories(destDir, "New_*").Where(d => OutputFolderName().IsMatch(Path.GetFileName(d)))
            : [];
}
