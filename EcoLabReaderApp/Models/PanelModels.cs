namespace EcoLabReaderApp.Models;

public class PanelTriplet
{
    public string CommonKey { get; set; } = string.Empty;
    public string RawTifPath { get; set; } = string.Empty;
    public string InfoElPath { get; set; } = string.Empty;
    public string MarkedTifPath { get; set; } = string.Empty;

    public bool IsComplete => !string.IsNullOrEmpty(RawTifPath) &&
                              !string.IsNullOrEmpty(InfoElPath) &&
                              !string.IsNullOrEmpty(MarkedTifPath);
}

public class ElPanelInfo
{
    public string PanelId { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string Timestamp { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // PASS / FAIL
    public bool IsDefective { get; set; }
    public List<string> Defects { get; set; } = new();
    public string FolderPath { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
}

public class AuditRecord
{
    public string PanelId { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string FolderName { get; set; } = string.Empty;
    public bool IsMatched { get; set; }
    public List<string> ElDefects { get; set; } = new();
    public List<string> HumanCorrections { get; set; } = new();
    public DateTime AuditedAt { get; set; } = DateTime.Now;
}

public class AuditSummary
{
    public int TotalAudited { get; set; }
    public int MatchedCount { get; set; }
    public int MismatchedCount { get; set; }
    public double AccuracyPercentage => TotalAudited > 0 ? Math.Round((double)MatchedCount / TotalAudited * 100.0, 2) : 0;
}

public class AuditSaveRequest
{
    public string FolderName { get; set; } = string.Empty;
    public string PanelId { get; set; } = string.Empty;
    public bool IsMatched { get; set; }
    public List<string> HumanCorrections { get; set; } = new();
}
