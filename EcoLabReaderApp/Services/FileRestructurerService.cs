using System.Text.RegularExpressions;
using EcoLabReaderApp.Models;

namespace EcoLabReaderApp.Services;

public class FileRestructurerService
{
    private readonly List<string> _containerPaths = new();
    private readonly string _restructuredPath;

    public FileRestructurerService(IWebHostEnvironment env)
    {
        string parentDir = Path.GetFullPath(Path.Combine(env.ContentRootPath, ".."));
        
        string parentContainer = Path.Combine(parentDir, "container");
        string localContainer = Path.Combine(env.ContentRootPath, "container");

        _containerPaths.Add(parentContainer);
        if (!parentContainer.Equals(localContainer, StringComparison.OrdinalIgnoreCase))
        {
            _containerPaths.Add(localContainer);
        }

        _restructuredPath = Path.Combine(parentDir, "Restructured");

        EnsureDirectoriesExist();
    }

    public string ContainerPath => _containerPaths.FirstOrDefault(Directory.Exists) ?? _containerPaths[0];
    public string RestructuredPath => _restructuredPath;

    public void EnsureDirectoriesExist()
    {
        foreach (var path in _containerPaths)
        {
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        }
        if (!Directory.Exists(_restructuredPath))
        {
            Directory.CreateDirectory(_restructuredPath);
        }
    }

    public int RunRestructuring()
    {
        EnsureDirectoriesExist();

        int totalOrganized = 0;

        foreach (var containerPath in _containerPaths)
        {
            if (!Directory.Exists(containerPath)) continue;

            var allFiles = Directory.GetFiles(containerPath, "*.*", SearchOption.AllDirectories)
                                    .Where(IsSupportedFile)
                                    .ToList();

            if (allFiles.Count == 0) continue;

            var triplets = MatchTriplets(allFiles, containerPath);

            foreach (var triplet in triplets)
            {
                if (!triplet.IsComplete)
                {
                    continue;
                }

                try
                {
                    // Get timestamp from .el file if present, or file creation time
                    string timestampFolder = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    if (!string.IsNullOrEmpty(triplet.InfoElPath) && File.Exists(triplet.InfoElPath))
                    {
                        var elFileInfo = new FileInfo(triplet.InfoElPath);
                        timestampFolder = elFileInfo.LastWriteTime.ToString("yyyy-MM-dd_HH-mm-ss");
                    }

                    string targetFolder = Path.Combine(_restructuredPath, timestampFolder);
                    
                    if (Directory.Exists(targetFolder))
                    {
                        targetFolder = Path.Combine(_restructuredPath, $"{timestampFolder}_{triplet.CommonKey}");
                    }

                    Directory.CreateDirectory(targetFolder);

                    string targetRawTif = Path.Combine(targetFolder, "row.tif");
                    string targetInfoEl = Path.Combine(targetFolder, "info.el");
                    string targetMarkedTif = Path.Combine(targetFolder, "marked.tif");

                    if (!string.IsNullOrEmpty(triplet.RawTifPath)) SafeMoveFile(triplet.RawTifPath, targetRawTif);
                    if (!string.IsNullOrEmpty(triplet.InfoElPath)) SafeMoveFile(triplet.InfoElPath, targetInfoEl);
                    if (!string.IsNullOrEmpty(triplet.MarkedTifPath)) SafeMoveFile(triplet.MarkedTifPath, targetMarkedTif);

                    totalOrganized++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error organizing triplet {triplet.CommonKey}: {ex.Message}");
                }
            }

            CleanEmptyFolders(containerPath);
        }

        return totalOrganized;
    }

    private bool IsSupportedFile(string filePath)
    {
        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext == ".el" || ext == ".tif" || ext == ".tiff" || ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
    }

    private void SafeMoveFile(string source, string destination)
    {
        if (!File.Exists(source)) return;

        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        try
        {
            File.Move(source, destination);
        }
        catch
        {
            File.Copy(source, destination, true);
            File.Delete(source);
        }
    }

    private List<PanelTriplet> MatchTriplets(List<string> filePaths, string rootContainerPath)
    {
        var dict = new Dictionary<string, PanelTriplet>(StringComparer.OrdinalIgnoreCase);

        // 1. Key-Based Extraction Strategy
        foreach (var filePath in filePaths)
        {
            string fileName = Path.GetFileName(filePath);

            var digitMatch = Regex.Match(fileName, @"(\d{4,6})", RegexOptions.IgnoreCase);
            if (!digitMatch.Success) continue;

            string key = digitMatch.Groups[1].Value;
            var triplet = GetOrCreateTriplet(dict, key);

            if (fileName.EndsWith(".el", StringComparison.OrdinalIgnoreCase))
            {
                triplet.InfoElPath = filePath;
            }
            else if (Regex.IsMatch(fileName, @"\.1\.(tif|tiff|png|jpg|jpeg|bmp)$", RegexOptions.IgnoreCase) || Regex.IsMatch(fileName, @"_1\.(tif|tiff|png|jpg|jpeg|bmp)$", RegexOptions.IgnoreCase))
            {
                triplet.RawTifPath = filePath;
            }
            else
            {
                if (string.IsNullOrEmpty(triplet.MarkedTifPath))
                {
                    triplet.MarkedTifPath = filePath;
                }
            }
        }

        // 2. Subdirectory Grouping Strategy for remaining unmatched files
        var subdirs = filePaths.Select(Path.GetDirectoryName)
                               .Distinct()
                               .Where(d => !string.IsNullOrEmpty(d) && !d.Equals(rootContainerPath, StringComparison.OrdinalIgnoreCase))
                               .ToList();

        foreach (var dir in subdirs)
        {
            var dirFiles = filePaths.Where(f => Path.GetDirectoryName(f) == dir).ToList();
            var elFiles = dirFiles.Where(f => f.EndsWith(".el", StringComparison.OrdinalIgnoreCase)).ToList();
            var imgFiles = dirFiles.Where(f => f != elFiles.FirstOrDefault() && IsSupportedFile(f)).ToList();

            if (elFiles.Count > 0 && imgFiles.Count >= 1)
            {
                string elFile = elFiles[0];
                string key = Path.GetFileNameWithoutExtension(elFile);
                var triplet = GetOrCreateTriplet(dict, key);

                triplet.InfoElPath = elFile;

                if (string.IsNullOrEmpty(triplet.RawTifPath) && imgFiles.Count > 0)
                {
                    triplet.RawTifPath = imgFiles[0];
                }

                if (string.IsNullOrEmpty(triplet.MarkedTifPath) && imgFiles.Count > 1)
                {
                    triplet.MarkedTifPath = imgFiles[1];
                }
                else if (string.IsNullOrEmpty(triplet.MarkedTifPath) && imgFiles.Count == 1)
                {
                    // Fallback to same image if only one image provided
                    triplet.MarkedTifPath = imgFiles[0];
                }
            }
        }

        // Resolve missing RawTif / MarkedTif swaps if RawTif was set to MarkedTif
        foreach (var t in dict.Values)
        {
            if (string.IsNullOrEmpty(t.RawTifPath) && !string.IsNullOrEmpty(t.MarkedTifPath))
            {
                t.RawTifPath = t.MarkedTifPath;
            }
            else if (string.IsNullOrEmpty(t.MarkedTifPath) && !string.IsNullOrEmpty(t.RawTifPath))
            {
                t.MarkedTifPath = t.RawTifPath;
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
