using System.Text.RegularExpressions;
using EcoLabReaderApp.Models;

namespace EcoLabReaderApp.Services;

public class FileRestructurerService
{
    private readonly string _containerPath;
    private readonly string _restructuredPath;
    private readonly string _goodModelsPath;
    private readonly string _badModelsPath;
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

        _goodModelsPath = Path.Combine(_restructuredPath, "Good_models");
        _badModelsPath = Path.Combine(_restructuredPath, "bad_models");

        EnsureDirectoriesExist();
    }

    public string ContainerPath => _containerPath;
    public string RestructuredPath => _restructuredPath;
    public string GoodModelsPath => _goodModelsPath;
    public string BadModelsPath => _badModelsPath;
    public string ReEvaluationPath => _reEvaluationPath;
    public string UselessPath => _uselessPath;

    public void EnsureDirectoriesExist()
    {
        if (!Directory.Exists(_containerPath)) Directory.CreateDirectory(_containerPath);
        if (!Directory.Exists(_restructuredPath)) Directory.CreateDirectory(_restructuredPath);
        if (!Directory.Exists(_goodModelsPath)) Directory.CreateDirectory(_goodModelsPath);
        if (!Directory.Exists(_badModelsPath)) Directory.CreateDirectory(_badModelsPath);
        if (!Directory.Exists(_reEvaluationPath)) Directory.CreateDirectory(_reEvaluationPath);
        if (!Directory.Exists(_uselessPath)) Directory.CreateDirectory(_uselessPath);

        FlattenNestedDirectories(_reEvaluationPath);
        FlattenNestedDirectories(_uselessPath);
        FlattenNestedDirectories(_goodModelsPath);
        FlattenNestedDirectories(_badModelsPath);
        FlattenNestedDirectories(_restructuredPath);
    }

    public void FlattenNestedDirectories(string basePath)
    {
        if (!Directory.Exists(basePath)) return;

        try
        {
            var topDirs = Directory.GetDirectories(basePath).ToList();
            foreach (var topDir in topDirs)
            {
                if (!Directory.Exists(topDir)) continue;

                var subDirs = Directory.GetDirectories(topDir).ToList();
                foreach (var subDir in subDirs)
                {
                    string subName = Path.GetFileName(subDir);
                    string targetPath = Path.Combine(basePath, subName);

                    bool isPanelFolder = File.Exists(Path.Combine(subDir, "info.el")) ||
                                         File.Exists(Path.Combine(subDir, "row.tif"));

                    if (isPanelFolder)
                    {
                        if (Path.GetFullPath(subDir).Equals(Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (Directory.Exists(targetPath))
                        {
                            DateTime subTime = Directory.GetLastWriteTimeUtc(subDir);
                            DateTime targetTime = Directory.GetLastWriteTimeUtc(targetPath);

                            if (subTime > targetTime)
                            {
                                Directory.Delete(targetPath, true);
                                SafeMoveDirectory(subDir, targetPath);
                            }
                            else
                            {
                                Directory.Delete(subDir, true);
                            }
                        }
                        else
                        {
                            SafeMoveDirectory(subDir, targetPath);
                        }
                    }
                }

                if (!File.Exists(Path.Combine(topDir, "info.el")) && !File.Exists(Path.Combine(topDir, "row.tif")))
                {
                    if (Directory.GetFiles(topDir).Length == 0 && Directory.GetDirectories(topDir).Length == 0)
                    {
                        Directory.Delete(topDir, true);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error flattening {basePath}: {ex.Message}");
        }
    }

    public string? FindPanelFolderPath(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return null;

        string direct = Path.Combine(_restructuredPath, folderName);
        if (Directory.Exists(direct)) return direct;

        string good = Path.Combine(_goodModelsPath, folderName);
        if (Directory.Exists(good)) return good;

        string bad = Path.Combine(_badModelsPath, folderName);
        if (Directory.Exists(bad)) return bad;

        string reeval = Path.Combine(_reEvaluationPath, folderName);
        if (Directory.Exists(reeval)) return reeval;

        string useless = Path.Combine(_uselessPath, folderName);
        if (Directory.Exists(useless)) return useless;

        return null;
    }

    public bool RestorePanelToModels(string folderName, ElParserService parser)
    {
        EnsureDirectoriesExist();
        string? source = FindPanelFolderPath(folderName);
        if (source == null || !Directory.Exists(source)) return false;

        string infoElPath = Path.Combine(source, "info.el");
        var info = parser.ParseElFile(infoElPath, folderName);

        string targetParent = (info.IsDefective || info.Defects.Count > 0) ? _badModelsPath : _goodModelsPath;
        string target = Path.Combine(targetParent, folderName);

        if (Path.GetFullPath(source).Equals(Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            return true;

        try
        {
            if (Directory.Exists(target))
            {
                Directory.Delete(target, true);
            }
            Directory.Move(source, target);
            return true;
        }
        catch
        {
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

    public bool MoveToReEvaluation(string folderName)
    {
        EnsureDirectoriesExist();
        string? source = FindPanelFolderPath(folderName);
        if (source == null || !Directory.Exists(source)) return false;

        string target = Path.Combine(_reEvaluationPath, folderName);

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
        string? source = FindPanelFolderPath(folderName);
        if (source == null || !Directory.Exists(source)) return false;

        string target = Path.Combine(_uselessPath, folderName);

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

    public bool HasFilesToProcess()
    {
        if (!Directory.Exists(_containerPath)) return false;
        return Directory.EnumerateFileSystemEntries(_containerPath).Any();
    }

    public int RunFullRestructuringAndPartitioning(ElParserService parser)
    {
        EnsureDirectoriesExist();

        // Only run restructuring if container has files
        int organizedCount = 0;
        if (HasFilesToProcess())
        {
            organizedCount = RunRestructuring();
        }

        // Scan ONLY unpartitioned root Restructured/ folders
        var unpartitionedFolders = new List<string>();
        foreach (var dir in Directory.GetDirectories(_restructuredPath))
        {
            string name = Path.GetFileName(dir);
            if (name.Equals("Good_models", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("bad_models", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Re_evaluation", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Useless", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            unpartitionedFolders.Add(dir);
        }

        // Partition ONLY new unpartitioned panel folders
        foreach (var panelFolder in unpartitionedFolders)
        {
            string infoElPath = Path.Combine(panelFolder, "info.el");
            if (!File.Exists(infoElPath)) continue;

            string folderName = Path.GetFileName(panelFolder);
            var info = parser.ParseElFile(infoElPath, folderName);

            string targetParentDir = (info.IsDefective || info.Defects.Count > 0) ? _badModelsPath : _goodModelsPath;
            string targetPath = Path.Combine(targetParentDir, folderName);

            SafeMoveDirectory(panelFolder, targetPath);
        }

        return organizedCount;
    }

    private void SafeMoveDirectory(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(sourceDir)) return;
        if (Path.GetFullPath(sourceDir).Equals(Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, true);
            }
            Directory.Move(sourceDir, targetDir);
        }
        catch
        {
            try
            {
                CopyDirectory(sourceDir, targetDir);
                Directory.Delete(sourceDir, true);
            }
            catch { }
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

                // Move files atomically & instantly (0 extra space required)
                SafeMoveFile(triplet.RawTifPath, targetRawTif);
                SafeMoveFile(triplet.InfoElPath, targetInfoEl);

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
            else if (fileName.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
            {
                triplet.RawTifPath = filePath;
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

            if (elFiles.Count >= 1 && tifFiles.Count >= 1)
            {
                string elFile = elFiles[0];
                string key = Path.GetFileNameWithoutExtension(elFile);

                var triplet = GetOrCreateTriplet(dict, key);
                triplet.InfoElPath = elFile;
                triplet.RawTifPath = tifFiles[0];
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
