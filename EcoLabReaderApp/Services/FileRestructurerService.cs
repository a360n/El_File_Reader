using System.Text.RegularExpressions;
using EcoLabReaderApp.Models;

namespace EcoLabReaderApp.Services;

public class FileRestructurerService
{
    private readonly string _containerPath;
    private readonly string _restructuredPath;
    private readonly string _reEvaluationPath;
    private readonly string _uselessPath;

    public FileRestructurerService(IWebHostEnvironment env)
    {
        string parentDir = Path.GetFullPath(Path.Combine(env.ContentRootPath, ".."));
        
        string candidateContainer = Path.Combine(parentDir, "container");
        string candidateRestructured = Path.Combine(parentDir, "Restructured");
        string candidateReEvaluation = Path.Combine(parentDir, "Re_evaluation");
        string candidateUseless = Path.Combine(parentDir, "Useless");

        if (!Directory.Exists(candidateContainer) && Directory.Exists(Path.Combine(env.ContentRootPath, "container")))
        {
            _containerPath = Path.Combine(env.ContentRootPath, "container");
            _restructuredPath = Path.Combine(env.ContentRootPath, "Restructured");
            _reEvaluationPath = Path.Combine(env.ContentRootPath, "Re_evaluation");
            _uselessPath = Path.Combine(env.ContentRootPath, "Useless");
        }
        else
        {
            _containerPath = candidateContainer;
            _restructuredPath = candidateRestructured;
            _reEvaluationPath = candidateReEvaluation;
            _uselessPath = candidateUseless;
        }

        EnsureDirectoriesExist();
    }

    public string ContainerPath => _containerPath;
    public string RestructuredPath => _restructuredPath;
    public string ReEvaluationPath => _reEvaluationPath;
    public string UselessPath => _uselessPath;

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
        if (!Directory.Exists(_reEvaluationPath))
        {
            Directory.CreateDirectory(_reEvaluationPath);
        }
        if (!Directory.Exists(_uselessPath))
        {
            Directory.CreateDirectory(_uselessPath);
        }
    }

    public bool MoveToReEvaluation(string folderName)
    {
        EnsureDirectoriesExist();
        string source = Path.Combine(_restructuredPath, folderName);
        string target = Path.Combine(_reEvaluationPath, folderName);

        if (!Directory.Exists(source)) return false;

        try
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }

            Directory.Move(source, target);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error moving {folderName} to Re_evaluation: {ex.Message}");
            try
            {
                CopyDirectory(source, target);
                Directory.Delete(source, true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public bool MoveToUseless(string folderName)
    {
        EnsureDirectoriesExist();
        string source = Path.Combine(_restructuredPath, folderName);
        string target = Path.Combine(_uselessPath, folderName);

        if (!Directory.Exists(source)) return false;

        try
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }

            Directory.Move(source, target);
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error moving {folderName} to Useless: {ex.Message}");
            try
            {
                CopyDirectory(source, target);
                Directory.Delete(source, true);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    private void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string dest = Path.Combine(destinationDir, Path.GetFileName(file));
            File.Copy(file, dest, true);
        }

        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string dest = Path.Combine(destinationDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, dest);
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

        if (allFiles.Count == 0)
        {
            CleanEmptyFolders(_containerPath);
            return 0;
        }

        var triplets = MatchTriplets(allFiles);
        int organizedCount = 0;

        foreach (var triplet in triplets)
        {
            if (!triplet.IsComplete)
            {
                // Ignore incomplete triplets
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

                // Move files atomically & instantly (0 extra space required)
                SafeMoveFile(triplet.RawTifPath, targetRawTif);
                SafeMoveFile(triplet.InfoElPath, targetInfoEl);
                SafeMoveFile(triplet.MarkedTifPath, targetMarkedTif);

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

    private void SafeMoveFile(string source, string destination)
    {
        if (!File.Exists(source)) return;

        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        try
        {
            // Move is instantaneous on the same drive & uses 0 extra space
            File.Move(source, destination);
        }
        catch
        {
            // Fallback if cross-drive move
            File.Copy(source, destination, true);
            File.Delete(source);
        }
    }

    private List<PanelTriplet> MatchTriplets(List<string> filePaths)
    {
        var dict = new Dictionary<string, PanelTriplet>(StringComparer.OrdinalIgnoreCase);

        // Strategy 1: Global Key Matching (matching panel digits e.g. 51410, 79360, etc.)
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
            else if (Regex.IsMatch(fileName, @"\.1\.tif$", RegexOptions.IgnoreCase) || Regex.IsMatch(fileName, @"_1\.tif$", RegexOptions.IgnoreCase))
            {
                triplet.RawTifPath = filePath;
            }
            else if (fileName.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
            {
                triplet.MarkedTifPath = filePath;
            }
        }

        // Strategy 2: Subfolder Grouping Fallback
        var subfolders = filePaths.Select(Path.GetDirectoryName)
                                  .Distinct()
                                  .Where(d => !string.IsNullOrEmpty(d) && !d.Equals(_containerPath, StringComparison.OrdinalIgnoreCase))
                                  .ToList();

        foreach (var subDir in subfolders)
        {
            var dirFiles = filePaths.Where(f => Path.GetDirectoryName(f) == subDir).ToList();
            var elFiles = dirFiles.Where(f => f.EndsWith(".el", StringComparison.OrdinalIgnoreCase)).ToList();
            var tifFiles = dirFiles.Where(f => f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase)).ToList();

            if (elFiles.Count == 1 && tifFiles.Count == 2)
            {
                string elFile = elFiles[0];
                string key = Path.GetFileNameWithoutExtension(elFile);

                var triplet = GetOrCreateTriplet(dict, key);
                triplet.InfoElPath = elFile;

                var raw = tifFiles.FirstOrDefault(f => Path.GetFileName(f).EndsWith(".1.tif", StringComparison.OrdinalIgnoreCase)) ?? tifFiles[0];
                var marked = tifFiles.FirstOrDefault(f => f != raw) ?? tifFiles[1];

                triplet.RawTifPath = raw;
                triplet.MarkedTifPath = marked;
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

                // Delete directory if no valid panel files remain inside it
                var validFiles = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                                          .Where(f => f.EndsWith(".el", StringComparison.OrdinalIgnoreCase) ||
                                                      f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
                                                      f.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
                                          .ToList();

                if (validFiles.Count == 0)
                {
                    try { Directory.Delete(directory, true); } catch { }
                }
            }
        }
        catch { }
    }
}
