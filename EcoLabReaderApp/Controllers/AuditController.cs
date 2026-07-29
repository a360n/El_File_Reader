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
        if (_restructurer.HasFilesToProcess())
        {
            _restructurer.RunFullRestructuringAndPartitioning(_parser);
        }

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
        if (_restructurer.HasFilesToProcess())
        {
            _restructurer.RunFullRestructuringAndPartitioning(_parser);
        }

        string goodPath = _restructurer.GoodModelsPath;
        var goodFolders = System.IO.Directory.Exists(goodPath)
            ? System.IO.Directory.GetDirectories(goodPath).OrderBy(d => System.IO.Path.GetFileName(d)).ToList()
            : new List<string>();

        if (goodFolders.Count == 0)
        {
            ViewBag.Message = "لا توجد أية ألواح سليمة نموذجية حالياً في مجلدات الفحص (Good_models).";
            return View("EmptyState");
        }

        if (panelIndex < 0) panelIndex = 0;
        if (panelIndex >= goodFolders.Count) panelIndex = goodFolders.Count - 1;

        string currentFolder = goodFolders[panelIndex];
        string infoElPath = System.IO.Path.Combine(currentFolder, "info.el");
        string folderName = System.IO.Path.GetFileName(currentFolder);

        var elInfo = _parser.ParseElFile(infoElPath, folderName);
        var existingRecord = _auditStorage.GetRecord(folderName);

        ViewBag.CurrentIndex = panelIndex;
        ViewBag.TotalGoodPanels = goodFolders.Count;
        ViewBag.HasPrevious = panelIndex > 0;
        ViewBag.HasNext = panelIndex < goodFolders.Count - 1;
        ViewBag.ExistingRecord = existingRecord;

        return View("GoodPanels", elInfo);
    }

    public IActionResult DefectivePanels(int panelIndex = 0)
    {
        if (_restructurer.HasFilesToProcess())
        {
            _restructurer.RunFullRestructuringAndPartitioning(_parser);
        }

        string badPath = _restructurer.BadModelsPath;
        var defectiveFolders = System.IO.Directory.Exists(badPath)
            ? System.IO.Directory.GetDirectories(badPath).OrderBy(d => System.IO.Path.GetFileName(d)).ToList()
            : new List<string>();

        if (defectiveFolders.Count == 0)
        {
            return View("NoDefectsState");
        }

        int totalDefectiveCellsCount = 0;
        foreach (var folder in defectiveFolders)
        {
            string infoPath = System.IO.Path.Combine(folder, "info.el");
            if (System.IO.File.Exists(infoPath))
            {
                string fName = System.IO.Path.GetFileName(folder);
                var info = _parser.ParseElFile(infoPath, fName);
                totalDefectiveCellsCount += info.Defects.Count;
            }
        }

        if (panelIndex < 0) panelIndex = 0;
        if (panelIndex >= defectiveFolders.Count) panelIndex = defectiveFolders.Count - 1;

        string currentFolder = defectiveFolders[panelIndex];
        string infoElPath = System.IO.Path.Combine(currentFolder, "info.el");
        string folderName = System.IO.Path.GetFileName(currentFolder);

        var elInfo = _parser.ParseElFile(infoElPath, folderName);
        var existingRecord = _auditStorage.GetRecord(folderName);

        ViewBag.CurrentIndex = panelIndex;
        ViewBag.TotalDefectivePanels = defectiveFolders.Count;
        ViewBag.TotalDefectiveCells = totalDefectiveCellsCount;
        ViewBag.HasPrevious = panelIndex > 0;
        ViewBag.HasNext = panelIndex < defectiveFolders.Count - 1;
        ViewBag.ExistingRecord = existingRecord;

        return View("DefectivePanels", elInfo);
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

    public IActionResult ReEvaluationPanels(int panelIndex = 0)
    {
        string path = _restructurer.ReEvaluationPath;
        var folders = System.IO.Directory.Exists(path)
            ? System.IO.Directory.GetDirectories(path).OrderBy(d => System.IO.Path.GetFileName(d)).ToList()
            : new List<string>();

        if (folders.Count == 0)
        {
            ViewBag.Message = "لا توجد أية ألواح حالياً في مجلد إعادة التقييم (Re_evaluation).";
            return View("EmptyState");
        }

        if (panelIndex < 0) panelIndex = 0;
        if (panelIndex >= folders.Count) panelIndex = folders.Count - 1;

        string currentFolder = folders[panelIndex];
        string infoElPath = System.IO.Path.Combine(currentFolder, "info.el");
        string folderName = System.IO.Path.GetFileName(currentFolder);

        var elInfo = _parser.ParseElFile(infoElPath, folderName);

        ViewBag.CurrentIndex = panelIndex;
        ViewBag.TotalPanels = folders.Count;
        ViewBag.HasPrevious = panelIndex > 0;
        ViewBag.HasNext = panelIndex < folders.Count - 1;

        return View("ReEvaluationPanels", elInfo);
    }

    public IActionResult UselessPanels(int panelIndex = 0)
    {
        string path = _restructurer.UselessPath;
        var folders = System.IO.Directory.Exists(path)
            ? System.IO.Directory.GetDirectories(path).OrderBy(d => System.IO.Path.GetFileName(d)).ToList()
            : new List<string>();

        if (folders.Count == 0)
        {
            ViewBag.Message = "لا توجد أية ألواح حالياً في مجلد الألواح التالفة (Useless).";
            return View("EmptyState");
        }

        if (panelIndex < 0) panelIndex = 0;
        if (panelIndex >= folders.Count) panelIndex = folders.Count - 1;

        string currentFolder = folders[panelIndex];
        string infoElPath = System.IO.Path.Combine(currentFolder, "info.el");
        string folderName = System.IO.Path.GetFileName(currentFolder);

        var elInfo = _parser.ParseElFile(infoElPath, folderName);

        ViewBag.CurrentIndex = panelIndex;
        ViewBag.TotalPanels = folders.Count;
        ViewBag.HasPrevious = panelIndex > 0;
        ViewBag.HasNext = panelIndex < folders.Count - 1;

        return View("UselessPanels", elInfo);
    }

    [HttpPost]
    public IActionResult SaveCroppedImage([FromBody] CropSaveRequest request)
    {
        if (string.IsNullOrEmpty(request.FolderName) || string.IsNullOrEmpty(request.ImageBase64))
        {
            return BadRequest(new { success = false, message = "بيانات الصورة واللوح غير مكتملة." });
        }

        string? folderPath = _restructurer.FindPanelFolderPath(request.FolderName);
        if (folderPath == null || !System.IO.Directory.Exists(folderPath))
        {
            return BadRequest(new { success = false, message = $"لم يتم العثور على مجلد اللوح ({request.FolderName})." });
        }

        try
        {
            string base64Data = request.ImageBase64;
            if (base64Data.Contains(","))
            {
                base64Data = base64Data.Split(',')[1];
            }

            byte[] imageBytes = Convert.FromBase64String(base64Data);
            string rowTiffPath = System.IO.Path.Combine(folderPath, "row.tif");

            System.IO.File.WriteAllBytes(rowTiffPath, imageBytes);

            bool restored = _restructurer.RestorePanelToModels(request.FolderName, _parser);

            if (restored)
            {
                return Json(new { success = true, message = $"تم حفظ الصورة المقصوصة وإعادة اللوح ({request.FolderName}) لمجلد النماذج الرئيسي بنجاح!" });
            }
            else
            {
                return Json(new { success = false, message = "تم حفظ الصورة المقصوصة ولكن تعذر إرجاع اللوح إلى مجلد النماذج." });
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "حدث خطأ أثناء حفظ الصورة المقصوصة: " + ex.Message });
        }
    }

    public IActionResult Search(string query)
    {
        ViewBag.Query = query ?? string.Empty;
        var results = new List<SearchResultItem>();

        if (string.IsNullOrWhiteSpace(query))
        {
            return View("Search", results);
        }

        string q = query.Trim().ToLower();

        var categories = new Dictionary<string, string>
        {
            { "Good_models", _restructurer.GoodModelsPath },
            { "bad_models", _restructurer.BadModelsPath },
            { "Re_evaluation", _restructurer.ReEvaluationPath },
            { "Useless", _restructurer.UselessPath },
            { "Restructured (Root)", _restructurer.RestructuredPath }
        };

        foreach (var kvp in categories)
        {
            string categoryName = kvp.Key;
            string categoryPath = kvp.Value;

            if (!System.IO.Directory.Exists(categoryPath)) continue;

            var dirList = System.IO.Directory.GetDirectories(categoryPath)
                .OrderBy(d => System.IO.Path.GetFileName(d))
                .ToList();

            for (int i = 0; i < dirList.Count; i++)
            {
                string dir = dirList[i];
                string dirName = System.IO.Path.GetFileName(dir);

                if (categoryName.Contains("Root") && (
                    dirName.Equals("Good_models", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("bad_models", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("Re_evaluation", StringComparison.OrdinalIgnoreCase) ||
                    dirName.Equals("Useless", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // 1. Fast memory check on folder name first
                bool isMatch = dirName.ToLower().Contains(q);
                string infoPath = System.IO.Path.Combine(dir, "info.el");

                // 2. Quick raw text scan ONLY if folder name didn't match
                if (!isMatch && System.IO.File.Exists(infoPath))
                {
                    string rawText = System.IO.File.ReadAllText(infoPath);
                    isMatch = rawText.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
                }

                // 3. Parse info.el ONLY for matched folders!
                if (isMatch)
                {
                    var info = System.IO.File.Exists(infoPath) ? _parser.ParseElFile(infoPath, dirName) : new ElPanelInfo { FolderName = dirName };

                    string viewUrl = "/Audit/Index?panelIndex=0";
                    if (categoryName.Contains("Good_models"))
                        viewUrl = $"/Audit/GoodPanels?panelIndex={i}";
                    else if (categoryName.Contains("bad_models"))
                        viewUrl = $"/Audit/DefectivePanels?panelIndex={i}";
                    else if (categoryName.Contains("Re_evaluation"))
                        viewUrl = $"/Audit/ReEvaluationPanels?panelIndex={i}";
                    else if (categoryName.Contains("Useless"))
                        viewUrl = $"/Audit/UselessPanels?panelIndex={i}";
                    else
                        viewUrl = $"/Audit/Index?panelIndex={i}";

                    results.Add(new SearchResultItem
                    {
                        FolderName = dirName,
                        SerialNumber = info.SerialNumber,
                        Category = categoryName,
                        FullFolderPath = dir,
                        IsDefective = info.IsDefective,
                        Defects = info.Defects,
                        Timestamp = info.Timestamp,
                        ViewUrl = viewUrl
                    });
                }
            }
        }

        return View("Search", results);
    }

    [HttpGet]
    public IActionResult Image(string folderName)
    {
        if (string.IsNullOrEmpty(folderName)) return NotFound();

        string? folderPath = _restructurer.FindPanelFolderPath(folderName);
        if (folderPath == null) return NotFound();

        string tiffPath = System.IO.Path.Combine(folderPath, "row.tif");
        if (!System.IO.File.Exists(tiffPath))
        {
            var tifFiles = System.IO.Directory.GetFiles(folderPath, "*.tif");
            if (tifFiles.Length > 0) tiffPath = tifFiles[0];
            else return NotFound();
        }

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
