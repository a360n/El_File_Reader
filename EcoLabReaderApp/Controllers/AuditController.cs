using Microsoft.AspNetCore.Mvc;
using EcoLabReaderApp.Models;
using EcoLabReaderApp.Services;

namespace EcoLabReaderApp.Controllers;

public class AuditController : Controller
{
    private readonly FileRestructurerService _restructurer;
    private readonly ElParserService _parser;
    private readonly AuditStorageService _auditStorage;
    private readonly TiffImageService _imageService;
    private readonly PdfExportService _pdfService;

    public AuditController(
        FileRestructurerService restructurer,
        ElParserService parser,
        AuditStorageService auditStorage,
        TiffImageService imageService,
        PdfExportService pdfService)
    {
        _restructurer = restructurer;
        _parser = parser;
        _auditStorage = auditStorage;
        _imageService = imageService;
        _pdfService = pdfService;
    }

    public IActionResult Index(int panelIndex = 0)
    {
        // Check container & run restructuring if needed
        _restructurer.RunRestructuring();

        var folders = GetRestructuredFolders();

        if (folders.Count == 0)
        {
            ViewBag.Message = "لا توجد مجلدات مفروزة في مجلد (Restructured). يرجى وضع الملفات في مجلد (container).";
            ViewBag.ContainerPath = _restructurer.ContainerPath;
            return View("EmptyState");
        }

        if (panelIndex < 0) panelIndex = 0;
        if (panelIndex >= folders.Count) panelIndex = folders.Count - 1;

        string currentFolder = folders[panelIndex];
        string infoElPath = System.IO.Path.Combine(currentFolder, "info.el");
        string folderName = System.IO.Path.GetFileName(currentFolder);

        var elInfo = _parser.ParseElFile(infoElPath, folderName);
        var existingRecord = _auditStorage.GetRecord(folderName);

        ViewBag.CurrentIndex = panelIndex;
        ViewBag.TotalPanels = folders.Count;
        ViewBag.HasPrevious = panelIndex > 0;
        ViewBag.HasNext = panelIndex < folders.Count - 1;
        ViewBag.ExistingRecord = existingRecord;

        return View(elInfo);
    }

    [HttpPost]
    public IActionResult SaveDecision([FromBody] AuditSaveRequest request)
    {
        if (string.IsNullOrEmpty(request.FolderName))
        {
            return BadRequest(new { success = false, message = "FolderName is required" });
        }

        string folderPath = System.IO.Path.Combine(_restructurer.RestructuredPath, request.FolderName);
        string infoElPath = System.IO.Path.Combine(folderPath, "info.el");

        var elInfo = _parser.ParseElFile(infoElPath, request.FolderName);

        var record = new AuditRecord
        {
            FolderName = request.FolderName,
            PanelId = string.IsNullOrEmpty(request.PanelId) ? elInfo.PanelId : request.PanelId,
            SerialNumber = elInfo.SerialNumber,
            IsMatched = request.IsMatched,
            ElDefects = elInfo.Defects,
            HumanCorrections = request.IsMatched ? new List<string> { "مطابق للمقروء" } : (request.HumanCorrections ?? new List<string>()),
            AuditedAt = DateTime.Now
        };

        _auditStorage.SaveRecord(record);

        return Json(new { success = true, message = "تم حفظ القرار بنجاح" });
    }

    public IActionResult Log()
    {
        var records = _auditStorage.GetAllRecords();
        var summary = _auditStorage.GetSummary();

        ViewBag.Summary = summary;
        return View(records);
    }

    [HttpGet]
    public IActionResult Image(string folderName, string type)
    {
        if (string.IsNullOrEmpty(folderName)) return NotFound();

        string fileName = type?.ToLower() == "marked" ? "marked.tif" : "row.tif";
        string tiffPath = System.IO.Path.Combine(_restructurer.RestructuredPath, folderName, fileName);

        if (!System.IO.File.Exists(tiffPath)) return NotFound();

        var (imageBytes, contentType) = _imageService.ConvertTiffToImageBytes(tiffPath);
        if (imageBytes == null) return NotFound();

        return this.File(imageBytes, contentType);
    }

    [HttpGet]
    public IActionResult ExportPdf()
    {
        var records = _auditStorage.GetAllRecords();
        var summary = _auditStorage.GetSummary();

        byte[] pdfContent = _pdfService.GenerateAuditReportPdf(records, summary);
        return this.File(pdfContent, "text/html", "EL_Audit_Report.html");
    }

    [HttpPost]
    public IActionResult TriggerRestructure()
    {
        int count = _restructurer.RunRestructuring();
        return Json(new { success = true, count, message = $"تم إعادة هيكلة وفرز {count} ألواح بنجاح" });
    }

    private List<string> GetRestructuredFolders()
    {
        string path = _restructurer.RestructuredPath;
        if (!System.IO.Directory.Exists(path)) return new List<string>();

        return System.IO.Directory.GetDirectories(path)
                        .OrderBy(d => System.IO.Path.GetFileName(d))
                        .ToList();
    }
}
