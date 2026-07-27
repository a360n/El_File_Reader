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
        string baseDir = ResolveBaseDirectory(env);

        _containerPath = Path.Combine(baseDir, "container");
        _restructuredPath = Path.Combine(baseDir, "Restructured");
        _goodModelsPath = Path.Combine(_restructuredPath, "Good_models");
        _badModelsPath = Path.Combine(_restructuredPath, "bad_models");
        _reEvaluationPath = Path.Combine(baseDir, "Re_evaluation");
        _uselessPath = Path.Combine(baseDir, "Useless");

        EnsureDirectoriesExist();
    }

    private string ResolveBaseDirectory(IWebHostEnvironment env)
    {
        string contentRoot = env.ContentRootPath;
        string parent = Path.GetFullPath(Path.Combine(contentRoot, ".."));
        string grandParent = Path.GetFullPath(Path.Combine(contentRoot, "..", ".."));
        string cwd = Directory.GetCurrentDirectory();

        string[] candidates = new[] { grandParent, parent, contentRoot, cwd };

        // Priority 1: Pick candidate directory where 'container' exists AND contains files
        foreach (var dir in candidates)
        {
            string containerCandidate = Path.Combine(dir, "container");
            if (Directory.Exists(containerCandidate))
            {
                var files = Directory.GetFiles(containerCandidate, "*.*", SearchOption.AllDirectories);
                if (files.Length > 0)
                {
                    return dir;
                }
            }
        }

        // Priority 2: Pick candidate directory where 'container' or 'Restructured' already exists
        foreach (var dir in candidates)
        {
            string containerCandidate = Path.Combine(dir, "container");
            string restructuredCandidate = Path.Combine(dir, "Restructured");
            if (Directory.Exists(containerCandidate) || Directory.Exists(restructuredCandidate))
            {
                return dir;
            }
        }

        return parent;
    }

    public string ContainerPath => _containerPath;
    public string RestructuredPath => _restructuredPath;
    public string GoodModelsPath => _goodModelsPath;
    public string BadModelsPath => _badModelsPath;
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
        if (!Directory.Exists(_goodModelsPath))
        {
            Directory.CreateDirectory(_goodModelsPath);
        }
        if (!Directory.Exists(_badModelsPath))
        {
            Directory.CreateDirectory(_badModelsPath);
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

    public string? FindPanelFolderPath(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return null;

        string direct = Path.Combine(_restructuredPath, folderName);
        if (Directory.Exists(direct)) return direct;

        string good = Path.Combine(_goodModelsPath, folderName);
        if (Directory.Exists(good)) return good;

        string bad = Path.Combine(_badModelsPath, folderName);
        if (Directory.Exists(bad)) return bad;

        return null;
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

    public int RunFullRestructuringAndPartitioning(ElParserService parser)
    {
        // Step 1: Process & restructure any raw triplets from container/ into Restructured/
        int organizedCount = RunRestructuring();

        // Step 2: Ensure Good_models and bad_models exist inside Restructured/
        EnsureDirectoriesExist();

        // Step 3: Scan all panel folders inside Restructured/ (root, Good_models, bad_models)
        var allFolderPaths = new List<string>();

        // Root Restructured/ folders (excluding reserved folders)
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
            allFolderPaths.Add(dir);
        }

        // Also check if any existing folders inside Good_models or bad_models need checking
        if (Directory.Exists(_goodModelsPath))
        {
            foreach (var dir in Directory.GetDirectories(_goodModelsPath))
            {
                allFolderPaths.Add(dir);
            }
        }

        if (Directory.Exists(_badModelsPath))
        {
            foreach (var dir in Directory.GetDirectories(_badModelsPath))
            {
                allFolderPaths.Add(dir);
            }
        }

        // Distribute / Partition panel folders based on info.el defect analysis
        foreach (var panelFolder in allFolderPaths)
        {
            string infoElPath = Path.Combine(panelFolder, "info.el");
            if (!File.Exists(infoElPath)) continue;

            string folderName = Path.GetFileName(panelFolder);
            var info = parser.ParseElFile(infoElPath, folderName);

            string targetParentDir = (info.IsDefective || info.Defects.Count > 0) ? _badModelsPath : _goodModelsPath;
            string targetPath = Path.Combine(targetParentDir, folderName);

            // Move only if not already in the target path
            if (!Path.GetFullPath(panelFolder).Equals(Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                SafeMoveDirectory(panelFolder, targetPath);
            }
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

        int organizedCount = 0;

        // Pass A: Check if container or any subfolder contains pre-structured panel folders (folders containing info.el)
        try
        {
            var allSubDirs = Directory.GetDirectories(_containerPath, "*", SearchOption.AllDirectories);
            foreach (var dir in allSubDirs)
            {
                if (!Directory.Exists(dir)) continue;
                string infoEl = Path.Combine(dir, "info.el");
                if (File.Exists(infoEl))
                {
                    string folderName = Path.GetFileName(dir);
                    string targetDir = Path.Combine(_restructuredPath, folderName);
                    SafeMoveDirectory(dir, targetDir);
                    organizedCount++;
                }
            }
        }
        catch { }

        // Pass B: Scan all remaining loose files inside container/
        var allFiles = Directory.GetFiles(_containerPath, "*.*", SearchOption.AllDirectories)
                                .Where(f => f.EndsWith(".el", StringComparison.OrdinalIgnoreCase) ||
                                            f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) ||
                                            f.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase))
                                .ToList();

        if (allFiles.Count == 0)
        {
            CleanEmptyFolders(_containerPath);
            return organizedCount;
        }

        var triplets = MatchTriplets(allFiles);

        foreach (var triplet in triplets)
        {
            if (string.IsNullOrEmpty(triplet.InfoElPath) || string.IsNullOrEmpty(triplet.RawTifPath))
            {
                continue;
            }

            try
            {
                var elFileInfo = new FileInfo(triplet.InfoElPath);
                string timestampFolder = elFileInfo.LastWriteTime.ToString("yyyy-MM-dd_HH-mm-ss");

                string targetFolder = Path.Combine(_restructuredPath, timestampFolder);
                
                if (Directory.Exists(targetFolder))
                {
                    targetFolder = Path.Combine(_restructuredPath, $"{timestampFolder}_{triplet.CommonKey}");
                }

                Directory.CreateDirectory(targetFolder);

                string targetRawTif = Path.Combine(targetFolder, "row.tif");
                string targetInfoEl = Path.Combine(targetFolder, "info.el");
                string targetMarkedTif = Path.Combine(targetFolder, "marked.tif");

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
        var resultTriplets = new List<PanelTriplet>();
        var directoryGroups = filePaths.GroupBy(Path.GetDirectoryName);

        foreach (var group in directoryGroups)
        {
            var dirFiles = group.ToList();
            var elFiles = dirFiles.Where(f => f.EndsWith(".el", StringComparison.OrdinalIgnoreCase)).ToList();
            var tifFiles = dirFiles.Where(f => f.EndsWith(".tif", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".tiff", StringComparison.OrdinalIgnoreCase)).ToList();

            if (elFiles.Count == 0 || tifFiles.Count == 0) continue;

            // Case A: 1 .el file in this directory -> ALL tif files in this directory belong to this .el file!
            if (elFiles.Count == 1)
            {
                string elFile = elFiles[0];
                string key = Path.GetFileNameWithoutExtension(elFile);

                // Find RAW TIF (prefer .1.tif, _1.tif, row.tif, raw.tif)
                string rawTif = tifFiles.FirstOrDefault(f => 
                    Regex.IsMatch(Path.GetFileName(f), @"[\._]1\.tif$", RegexOptions.IgnoreCase) || 
                    Path.GetFileName(f).StartsWith("row", StringComparison.OrdinalIgnoreCase) || 
                    Path.GetFileName(f).StartsWith("raw", StringComparison.OrdinalIgnoreCase)
                ) ?? tifFiles[0];

                // Find Marked TIF (prefer different tif file)
                string markedTif = tifFiles.FirstOrDefault(f => !f.Equals(rawTif, StringComparison.OrdinalIgnoreCase)) ?? rawTif;

                resultTriplets.Add(new PanelTriplet
                {
                    CommonKey = key,
                    InfoElPath = elFile,
                    RawTifPath = rawTif,
                    MarkedTifPath = markedTif
                });
            }
            // Case B: Multiple .el files in the same directory -> Group by numeric key or prefix
            else
            {
                foreach (var elFile in elFiles)
                {
                    string elNameNoExt = Path.GetFileNameWithoutExtension(elFile);
                    var match = Regex.Match(elNameNoExt, @"(\d{1,10})");
                    string key = match.Success ? match.Groups[1].Value : elNameNoExt;

                    var matchingTifs = tifFiles.Where(f => f.Contains(key, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (matchingTifs.Count == 0) matchingTifs = tifFiles;

                    string rawTif = matchingTifs.FirstOrDefault(f => 
                        Regex.IsMatch(Path.GetFileName(f), @"[\._]1\.tif$", RegexOptions.IgnoreCase) || 
                        Path.GetFileName(f).StartsWith("row", StringComparison.OrdinalIgnoreCase) || 
                        Path.GetFileName(f).StartsWith("raw", StringComparison.OrdinalIgnoreCase)
                    ) ?? matchingTifs[0];

                    string markedTif = matchingTifs.FirstOrDefault(f => !f.Equals(rawTif, StringComparison.OrdinalIgnoreCase)) ?? rawTif;

                    resultTriplets.Add(new PanelTriplet
                    {
                        CommonKey = key,
                        InfoElPath = elFile,
                        RawTifPath = rawTif,
                        MarkedTifPath = markedTif
                    });
                }
            }
        }

        return resultTriplets.GroupBy(t => t.InfoElPath, StringComparer.OrdinalIgnoreCase)
                             .Select(g => g.First())
                             .ToList();
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
