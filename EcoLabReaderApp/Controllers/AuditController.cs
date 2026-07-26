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
    private readonly AiCellPipelineService _aiCellPipeline;

    public AuditController(
        FileRestructurerService restructurer,
        ElParserService parser,
        AuditStorageService auditStorage,
        TiffImageService imageService,
        PdfExportService pdfService,
        AiCellPipelineService aiCellPipeline)
    {
        _restructurer = restructurer;
        _parser = parser;
        _auditStorage = auditStorage;
        _imageService = imageService;
        _pdfService = pdfService;
        _aiCellPipeline = aiCellPipeline;
    }

    public IActionResult Index(int panelIndex = 0)
    {
        // Run container restructuring + automatic model partitioning into Good_models & bad_models
        _restructurer.RunFullRestructuringAndPartitioning(_parser);

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

    public IActionResult GoodPanels(int panelIndex = 0)
    {
        _restructurer.RunFullRestructuringAndPartitioning(_parser);
        var allFolders = GetRestructuredFolders();

        var goodList = new List<(string Folder, ElPanelInfo Info)>();

        foreach (var folder in allFolders)
        {
            string infoElPath = System.IO.Path.Combine(folder, "info.el");
            string folderName = System.IO.Path.GetFileName(folder);
            var info = _parser.ParseElFile(infoElPath, folderName);

            if (!info.IsDefective && info.Defects.Count == 0)
            {
                goodList.Add((folder, info));
            }
        }

        if (goodList.Count == 0)
        {
            ViewBag.Message = "لا توجد أية ألواح سليمة نموذجية حالياً في مجلدات الفحص.";
            return View("EmptyState");
        }

        if (panelIndex < 0) panelIndex = 0;
        if (panelIndex >= goodList.Count) panelIndex = goodList.Count - 1;

        var currentItem = goodList[panelIndex];
        var existingRecord = _auditStorage.GetRecord(currentItem.Info.FolderName);

        ViewBag.CurrentIndex = panelIndex;
        ViewBag.TotalGoodPanels = goodList.Count;
        ViewBag.HasPrevious = panelIndex > 0;
        ViewBag.HasNext = panelIndex < goodList.Count - 1;
        ViewBag.ExistingRecord = existingRecord;

        return View("GoodPanels", currentItem.Info);
    }

    public IActionResult DefectivePanels(int panelIndex = 0)
    {
        _restructurer.RunFullRestructuringAndPartitioning(_parser);
        var allFolders = GetRestructuredFolders();

        var defectiveList = new List<(string Folder, ElPanelInfo Info)>();
        int totalDefectiveCellsCount = 0;

        foreach (var folder in allFolders)
        {
            string infoElPath = System.IO.Path.Combine(folder, "info.el");
            string folderName = System.IO.Path.GetFileName(folder);
            var info = _parser.ParseElFile(infoElPath, folderName);

            if (info.IsDefective || info.Defects.Count > 0)
            {
                defectiveList.Add((folder, info));
                totalDefectiveCellsCount += info.Defects.Count;
            }
        }

        if (defectiveList.Count == 0)
        {
            return View("NoDefectsState");
        }

        if (panelIndex < 0) panelIndex = 0;
        if (panelIndex >= defectiveList.Count) panelIndex = defectiveList.Count - 1;

        var currentItem = defectiveList[panelIndex];
        var existingRecord = _auditStorage.GetRecord(currentItem.Info.FolderName);

        ViewBag.CurrentIndex = panelIndex;
        ViewBag.TotalDefectivePanels = defectiveList.Count;
        ViewBag.TotalDefectiveCells = totalDefectiveCellsCount;
        ViewBag.HasPrevious = panelIndex > 0;
        ViewBag.HasNext = panelIndex < defectiveList.Count - 1;
        ViewBag.ExistingRecord = existingRecord;

        return View("DefectivePanels", currentItem.Info);
    }

    [HttpPost]
    public IActionResult SaveDecision([FromBody] AuditSaveRequest request)
    {
        if (string.IsNullOrEmpty(request.FolderName))
        {
            return BadRequest(new { success = false, message = "FolderName is required" });
        }

        string folderPath = _restructurer.FindPanelFolderPath(request.FolderName) ?? System.IO.Path.Combine(_restructurer.RestructuredPath, request.FolderName);
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

    [HttpPost]
    public IActionResult MoveToReEvaluation([FromBody] AuditSaveRequest request)
    {
        if (string.IsNullOrEmpty(request.FolderName))
        {
            return BadRequest(new { success = false, message = "FolderName is required" });
        }

        bool moved = _restructurer.MoveToReEvaluation(request.FolderName);

        if (moved)
        {
            return Json(new { success = true, message = $"تم قص ونقل اللوح ({request.FolderName}) إلى مجلد Re_evaluation بنجاح" });
        }
        else
        {
            return Json(new { success = false, message = $"فشل نقل المجلد ({request.FolderName}) إلى Re_evaluation" });
        }
    }

    [HttpPost]
    public IActionResult MoveToUseless([FromBody] AuditSaveRequest request)
    {
        if (string.IsNullOrEmpty(request.FolderName))
        {
            return BadRequest(new { success = false, message = "FolderName is required" });
        }

        bool moved = _restructurer.MoveToUseless(request.FolderName);

        if (moved)
        {
            return Json(new { success = true, message = $"تم قص ونقل اللوح ({request.FolderName}) إلى مجلد Useless بنجاح" });
        }
        else
        {
            return Json(new { success = false, message = $"فشل نقل المجلد ({request.FolderName}) إلى Useless" });
        }
    }

    [HttpGet]
    public IActionResult Image(string folderName, string type)
    {
        if (string.IsNullOrEmpty(folderName)) return NotFound();

        string fileName = type?.ToLower() == "marked" ? "marked.tif" : "row.tif";
        string? folderPath = _restructurer.FindPanelFolderPath(folderName);
        if (folderPath == null) return NotFound();

        string tiffPath = System.IO.Path.Combine(folderPath, fileName);

        if (!System.IO.File.Exists(tiffPath)) return NotFound();

        var (imageBytes, contentType) = _imageService.ConvertTiffToImageBytes(tiffPath);
        if (imageBytes == null) return NotFound();

        return this.File(imageBytes, contentType);
    }

    [HttpPost]
    public IActionResult TriggerRestructure()
    {
        int count = _restructurer.RunFullRestructuringAndPartitioning(_parser);
        return Json(new { success = true, count, message = $"تم إعادة هيكلة وفرز وتوزيع {count} ألواح بنجاح" });
    }

    [HttpPost]
    public IActionResult TriggerAiCellPipeline()
    {
        var result = _aiCellPipeline.RunPipeline();
        return Json(result);
    }

    private List<string> GetRestructuredFolders()
    {
        string path = _restructurer.RestructuredPath;
        if (!System.IO.Directory.Exists(path)) return new List<string>();

        var result = new List<string>();

        // 1. Direct subdirectories in Restructured/
        foreach (var dir in System.IO.Directory.GetDirectories(path))
        {
            string name = System.IO.Path.GetFileName(dir);
            if (name.Equals("Good_models", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("bad_models", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Re_evaluation", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Useless", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (System.IO.File.Exists(System.IO.Path.Combine(dir, "info.el")))
            {
                result.Add(dir);
            }
        }

        // 2. Subdirectories in Restructured/Good_models/
        string goodPath = _restructurer.GoodModelsPath;
        if (System.IO.Directory.Exists(goodPath))
        {
            foreach (var dir in System.IO.Directory.GetDirectories(goodPath))
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "info.el")))
                {
                    result.Add(dir);
                }
            }
        }

        // 3. Subdirectories in Restructured/bad_models/
        string badPath = _restructurer.BadModelsPath;
        if (System.IO.Directory.Exists(badPath))
        {
            foreach (var dir in System.IO.Directory.GetDirectories(badPath))
            {
                if (System.IO.File.Exists(System.IO.Path.Combine(dir, "info.el")))
                {
                    result.Add(dir);
                }
            }
        }

        return result.OrderBy(d => System.IO.Path.GetFileName(d)).ToList();
    }
}
