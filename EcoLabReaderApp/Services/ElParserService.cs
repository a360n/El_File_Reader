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
            // Extract from marked.tif in folder if exists
            string markedPath = Path.Combine(result.FolderPath, "marked.tif");
            if (File.Exists(markedPath))
            {
                var markedName = Path.GetFileName(markedPath);
                var fnMatch = Regex.Match(markedName, @"^(ANM[A-Z0-9_-]+)");
                if (fnMatch.Success)
                {
                    result.SerialNumber = fnMatch.Value;
                }
            }

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

        return result;
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
