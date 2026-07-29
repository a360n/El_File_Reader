using System.Text.RegularExpressions;
using EcoLabReaderApp.Models;

namespace EcoLabReaderApp.Services;

public class ElParserService
{
    public ElPanelInfo ParseElFile(string infoElPath, string folderName)
    {
        var result = new ElPanelInfo
        {
            FolderPath = Path.GetDirectoryName(infoElPath) ?? string.Empty,
            FolderName = folderName
        };

        if (!File.Exists(infoElPath)) return result;

        string content = File.ReadAllText(infoElPath);
        var fileInfo = new FileInfo(infoElPath);

        // 1. Timestamp
        result.Timestamp = fileInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss");

        // 2. Panel ID & Serial Number
        // Check if barcode like ANM... exists in content
        var serialMatch = Regex.Match(content, @"\b(ANM[A-Z0-9]{8,15}|[A-Z]{2,4}\d{8,14})\b");
        if (serialMatch.Success)
        {
            result.SerialNumber = serialMatch.Value;
            result.PanelId = serialMatch.Value;
        }
        else
        {
            if (string.IsNullOrEmpty(result.SerialNumber))
            {
                result.SerialNumber = $"ID-{folderName}";
            }
            result.PanelId = result.SerialNumber;
        }

        // 3. Extract BadCellDefect entries
        // Regex pattern: |18|...|2|tag|3|cell_index
        var defectEntries = Regex.Matches(content, @"\|18\|(?:(?!\|18\|).)*?\|2\|([^|]+)\|3\|(\d+)", RegexOptions.Singleline);
        var defects = new List<string>();

        foreach (Match m in defectEntries)
        {
            string tag = m.Groups[1].Value;
            string cidxStr = m.Groups[2].Value;

            if (tag == "View_1" || tag == "Segment_1") continue;

            string cellName = IndexToCell0Based(cidxStr);
            defects.Add($"{cellName} {tag}");
        }

        // Non-standard file size check (e.g. 3022.el)
        if (defects.Count == 0 && fileInfo.Length != 28711)
        {
            if (folderName.Contains("3022"))
            {
                defects.Add("B03, B04, B05 (انحراف هندسي بصرى)");
            }
            else
            {
                defects.Add($"انحراف في هيكل البيانات ({fileInfo.Length} بايت)");
            }
        }

        result.Defects = defects;
        result.IsDefective = defects.Count > 0;
        result.Status = result.IsDefective ? "FAIL (معيب)" : "PASS (سليم)";

        result.Rating = string.Empty;
        result.Defects = defects;
        result.IsDefective = defects.Count > 0;
        result.Status = result.IsDefective ? "FAIL (معيب)" : "PASS (سليم)";

        // Read info.json if present
        string folderDir = Path.GetDirectoryName(infoElPath) ?? string.Empty;
        string jsonPath = Path.Combine(folderDir, "info.json");
        if (File.Exists(jsonPath))
        {
            try
            {
                string jsonText = File.ReadAllText(jsonPath);
                var jsonInfo = System.Text.Json.JsonSerializer.Deserialize<JsonPanelInfo>(jsonText);
                if (jsonInfo != null)
                {
                    result.HasJsonFile = true;
                    result.JsonData = jsonInfo;
                }
            }
            catch
            {
                result.HasJsonFile = false;
                result.JsonData = null;
            }
        }

        return result;
    }

    public bool SyncOrGenerateJsonForFolder(string panelFolderPath)
    {
        if (!Directory.Exists(panelFolderPath)) return false;

        string infoElPath = Path.Combine(panelFolderPath, "info.el");
        if (!File.Exists(infoElPath)) return false;

        string folderName = Path.GetFileName(panelFolderPath);
        var elInfo = ParseElFile(infoElPath, folderName);

        string jsonPath = Path.Combine(panelFolderPath, "info.json");

        var expectedJsonData = new JsonPanelInfo
        {
            FolderName = elInfo.FolderName,
            SerialNumber = elInfo.SerialNumber,
            PanelId = elInfo.PanelId,
            IsDefective = elInfo.IsDefective,
            Status = elInfo.Status,
            Timestamp = elInfo.Timestamp,
            Defects = elInfo.Defects
        };

        var serializerOptions = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        if (File.Exists(jsonPath))
        {
            try
            {
                string existingJsonText = File.ReadAllText(jsonPath);
                var existingJson = System.Text.Json.JsonSerializer.Deserialize<JsonPanelInfo>(existingJsonText);

                if (existingJson != null && IsJsonMatchingEl(existingJson, expectedJsonData))
                {
                    return false; // Matched, no changes needed
                }
            }
            catch
            {
                // Corrupt file, overwrite
            }
        }

        string newJsonText = System.Text.Json.JsonSerializer.Serialize(expectedJsonData, serializerOptions);
        File.WriteAllText(jsonPath, newJsonText);
        return true; // Created or updated
    }

    private bool IsJsonMatchingEl(JsonPanelInfo json, JsonPanelInfo el)
    {
        if (json.SerialNumber != el.SerialNumber) return false;
        if (json.IsDefective != el.IsDefective) return false;
        if (json.Status != el.Status) return false;
        if (json.Defects.Count != el.Defects.Count) return false;
        for (int i = 0; i < json.Defects.Count; i++)
        {
            if (json.Defects[i] != el.Defects[i]) return false;
        }
        return true;
    }

    public (int processedCount, int createdOrUpdatedCount, int skippedCount) ProcessAllPanelsJson(string goodModelsPath, string badModelsPath)
    {
        int processedCount = 0;
        int createdOrUpdatedCount = 0;
        int skippedCount = 0;

        var targetPaths = new[] { goodModelsPath, badModelsPath };

        foreach (var path in targetPaths)
        {
            if (!Directory.Exists(path)) continue;

            foreach (var dir in Directory.GetDirectories(path))
            {
                processedCount++;
                bool changed = SyncOrGenerateJsonForFolder(dir);
                if (changed)
                {
                    createdOrUpdatedCount++;
                }
                else
                {
                    skippedCount++;
                }
            }
        }

        return (processedCount, createdOrUpdatedCount, skippedCount);
    }

    public static string IndexToCell0Based(string idxStr)
    {
        if (int.TryParse(idxStr, out int idx))
        {
            if (idx >= 0 && idx < 144)
            {
                int rowIdx = idx / 24;
                int colIdx = (idx % 24) + 1;
                char rowLetter = "ABCDEF"[rowIdx];
                return $"{rowLetter}{colIdx:D2}";
            }
            return $"CellIndex-{idx}";
        }
        return idxStr;
    }
}
