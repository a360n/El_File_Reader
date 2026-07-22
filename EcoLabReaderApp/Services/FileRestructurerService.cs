using System.Text.RegularExpressions;
using EcoLabReaderApp.Models;

namespace EcoLabReaderApp.Services;

public class FileRestructurerService
{
    private readonly string _containerPath;
    private readonly string _restructuredPath;

    public FileRestructurerService(IWebHostEnvironment env)
    {
        // Target container and Restructured in the parent folder (solution root)
        string parentDir = Path.GetFullPath(Path.Combine(env.ContentRootPath, ".."));
        
        string candidateContainer = Path.Combine(parentDir, "container");
        string candidateRestructured = Path.Combine(parentDir, "Restructured");

        // If running directly inside app dir, fallback to ContentRootPath
        if (!Directory.Exists(candidateContainer) && Directory.Exists(Path.Combine(env.ContentRootPath, "container")))
        {
            _containerPath = Path.Combine(env.ContentRootPath, "container");
            _restructuredPath = Path.Combine(env.ContentRootPath, "Restructured");
        }
        else
        {
            _containerPath = candidateContainer;
            _restructuredPath = candidateRestructured;
        }

        EnsureDirectoriesExist();
    }

    public string ContainerPath => _containerPath;
    public string RestructuredPath => _restructuredPath;

    public void EnsureDirectoriesExist()
    {
        if (!Directory.Exists(_containerPath))
        {
            Directory.CreateDirectory(_containerPath);
        }
        if (!Directory.Exists(_restructuredPath))
        {
            Directory.CreateDirectory(_restructuredPath);
        }
    }

    public int RunRestructuring()
    {
        EnsureDirectoriesExist();

        if (!Directory.Exists(_containerPath)) return 0;

        var allFiles = Directory.GetFiles(_containerPath, "*.*", SearchOption.AllDirectories)
                                .Where(f => f.EndsWith(".el", StringComparison.OrdinalIgnoreCase) ||
                                            f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
                                            f.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
                                .ToList();

        if (allFiles.Count == 0) return 0;

        var triplets = MatchTriplets(allFiles);
        int organizedCount = 0;

        foreach (var triplet in triplets)
        {
            if (!triplet.IsComplete)
            {
                // Ignore incomplete triplets as requested
                continue;
            }

            try
            {
                // Get timestamp from .el file
                var elFileInfo = new FileInfo(triplet.InfoElPath);
                string timestampFolder = elFileInfo.LastWriteTime.ToString("yyyy-MM-dd_HH-mm-ss");

                string targetFolder = Path.Combine(_restructuredPath, timestampFolder);
                
                // If folder already exists, append unique suffix
                if (Directory.Exists(targetFolder))
                {
                    targetFolder = Path.Combine(_restructuredPath, $"{timestampFolder}_{triplet.CommonKey}");
                }

                Directory.CreateDirectory(targetFolder);

                // Target paths
                string targetRawTif = Path.Combine(targetFolder, "row.tif");
                string targetInfoEl = Path.Combine(targetFolder, "info.el");
                string targetMarkedTif = Path.Combine(targetFolder, "marked.tif");

                // Copy / Move files
                File.Copy(triplet.RawTifPath, targetRawTif, true);
                File.Copy(triplet.InfoElPath, targetInfoEl, true);
                File.Copy(triplet.MarkedTifPath, targetMarkedTif, true);

                // Delete originals from container
                TryDeleteFile(triplet.RawTifPath);
                TryDeleteFile(triplet.InfoElPath);
                TryDeleteFile(triplet.MarkedTifPath);

                organizedCount++;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error organizing triplet {triplet.CommonKey}: {ex.Message}");
            }
        }

        // Clean empty subfolders inside container
        CleanEmptyFolders(_containerPath);

        return organizedCount;
    }

    private List<PanelTriplet> MatchTriplets(List<string> filePaths)
    {
        var dict = new Dictionary<string, PanelTriplet>(StringComparer.OrdinalIgnoreCase);

        foreach (var filePath in filePaths)
        {
            string fileName = Path.GetFileName(filePath);

            // Pattern 1: Raw TIF (e.g. 51410.1.tif or 79422.1.tif)
            var rawMatch = Regex.Match(fileName, @"^(\d{4,6})\.1\.tif$", RegexOptions.IgnoreCase);
            if (rawMatch.Success)
            {
                string key = rawMatch.Groups[1].Value;
                GetOrCreateTriplet(dict, key).RawTifPath = filePath;
                continue;
            }

            // Pattern 2: Info EL (e.g. 51410.el or 79422.el)
            var infoMatch = Regex.Match(fileName, @"^(\d{4,6})\.el$", RegexOptions.IgnoreCase);
            if (infoMatch.Success)
            {
                string key = infoMatch.Groups[1].Value;
                GetOrCreateTriplet(dict, key).InfoElPath = filePath;
                continue;
            }

            // Pattern 3: Marked TIF (e.g. ANM26040006582_2026-04-01_16-01-05_51410-1.tif or ANM..._79422-1.tif)
            var markedMatch = Regex.Match(fileName, @"_(\d{4,6})-[0-9]+\.tif$", RegexOptions.IgnoreCase);
            if (!markedMatch.Success)
            {
                markedMatch = Regex.Match(fileName, @"_(\d{4,6})\.tif$", RegexOptions.IgnoreCase);
            }

            if (markedMatch.Success)
            {
                string key = markedMatch.Groups[1].Value;
                GetOrCreateTriplet(dict, key).MarkedTifPath = filePath;
                continue;
            }
        }

        return dict.Values.ToList();
    }

    private PanelTriplet GetOrCreateTriplet(Dictionary<string, PanelTriplet> dict, string key)
    {
        if (!dict.TryGetValue(key, out var triplet))
        {
            triplet = new PanelTriplet { CommonKey = key };
            dict[key] = triplet;
        }
        return triplet;
    }

    private void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }

    private void CleanEmptyFolders(string path)
    {
        try
        {
            foreach (var directory in Directory.GetDirectories(path))
            {
                CleanEmptyFolders(directory);
                if (Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0)
                {
                    Directory.Delete(directory, false);
                }
            }
        }
        catch { }
    }
}
