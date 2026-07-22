using System.Text.Json;
using EcoLabReaderApp.Models;

namespace EcoLabReaderApp.Services;

public class AuditStorageService
{
    private readonly string _logFilePath;
    private readonly object _lockObj = new();
    private List<AuditRecord> _records = new();

    public AuditStorageService(FileRestructurerService restructurer)
    {
        string restructuredFolder = restructurer.RestructuredPath;
        if (!Directory.Exists(restructuredFolder))
        {
            Directory.CreateDirectory(restructuredFolder);
        }
        _logFilePath = Path.Combine(restructuredFolder, "audit_log.json");
        LoadLog();
    }

    public List<AuditRecord> GetAllRecords()
    {
        lock (_lockObj)
        {
            return _records.OrderByDescending(r => r.AuditedAt).ToList();
        }
    }

    public AuditRecord? GetRecord(string folderName)
    {
        lock (_lockObj)
        {
            return _records.FirstOrDefault(r => r.FolderName.Equals(folderName, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void SaveRecord(AuditRecord record)
    {
        lock (_lockObj)
        {
            var existingIndex = _records.FindIndex(r => r.FolderName.Equals(record.FolderName, StringComparison.OrdinalIgnoreCase));
            if (existingIndex >= 0)
            {
                _records[existingIndex] = record;
            }
            else
            {
                _records.Add(record);
            }
            SaveLog();
        }
    }

    public AuditSummary GetSummary()
    {
        lock (_lockObj)
        {
            int total = _records.Count;
            int matched = _records.Count(r => r.IsMatched);
            int mismatched = total - matched;

            return new AuditSummary
            {
                TotalAudited = total,
                MatchedCount = matched,
                MismatchedCount = mismatched
            };
        }
    }

    private void LoadLog()
    {
        lock (_lockObj)
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    string json = File.ReadAllText(_logFilePath);
                    _records = JsonSerializer.Deserialize<List<AuditRecord>>(json) ?? new List<AuditRecord>();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading audit log: {ex.Message}");
                _records = new List<AuditRecord>();
            }
        }
    }

    private void SaveLog()
    {
        try
        {
            string json = JsonSerializer.Serialize(_records, new JsonSerializerOptions { WriteIndented = true });
            File.ReadAllText(_logFilePath); // Read test
            File.WriteAllText(_logFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error saving audit log: {ex.Message}");
        }
    }
}
