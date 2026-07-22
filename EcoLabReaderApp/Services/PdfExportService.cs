using System.Text;
using EcoLabReaderApp.Models;

namespace EcoLabReaderApp.Services;

public class PdfExportService
{
    public byte[] GenerateAuditReportPdf(List<AuditRecord> records, AuditSummary summary)
    {
        // Generates an HTML printable report formatted specifically for PDF rendering/printing
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html dir='rtl' lang='ar'>");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset='utf-8'>");
        sb.AppendLine("<title>تقرير سجل مطابقة وقراءة ملفات EL</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body { font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif; padding: 25px; background: #fff; color: #1e293b; }");
        sb.AppendLine(".header { text-align: center; border-bottom: 3px solid #2563eb; padding-bottom: 15px; margin-bottom: 25px; }");
        sb.AppendLine(".header h1 { color: #1e3a8a; margin: 0 0 10px 0; font-size: 26px; }");
        sb.AppendLine(".header p { color: #64748b; margin: 0; font-size: 14px; }");
        sb.AppendLine(".stats-card { background: #f8fafc; border: 1px solid #e2e8f0; border-radius: 12px; padding: 20px; margin-bottom: 30px; }");
        sb.AppendLine(".stats-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 15px; text-align: center; }");
        sb.AppendLine(".stat-item { background: #fff; padding: 15px; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,0.05); }");
        sb.AppendLine(".stat-val { font-size: 22px; font-weight: bold; color: #2563eb; margin-top: 5px; }");
        sb.AppendLine(".stat-val.acc { color: #059669; }");
        sb.AppendLine("table { width: 100%; border-collapse: collapse; margin-top: 15px; font-size: 13px; }");
        sb.AppendLine("th, td { border: 1px solid #cbd5e1; padding: 10px 12px; text-align: right; }");
        sb.AppendLine("th { background-color: #1e293b; color: #ffffff; font-weight: 600; }");
        sb.AppendLine("tr:nth-child(even) { background-color: #f8fafc; }");
        sb.AppendLine(".badge-pass { background: #d1fae5; color: #065f46; padding: 4px 8px; border-radius: 6px; font-weight: bold; }");
        sb.AppendLine(".badge-fail { background: #fee2e2; color: #991b1b; padding: 4px 8px; border-radius: 6px; font-weight: bold; }");
        sb.AppendLine("@media print { body { padding: 0; } }");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Header
        sb.AppendLine("<div class='header'>");
        sb.AppendLine("<h1>تقرير تدقيق ومطابقة قراءة ملفات EL والألواح الشمسية</h1>");
        sb.AppendLine($"<p>تاريخ التوليد: {DateTime.Now:yyyy-MM-dd HH:mm:ss} | نظام تدقيق البيانات للذكاء الاصطناعي</p>");
        sb.AppendLine("</div>");

        // Summary Card
        sb.AppendLine("<div class='stats-card'>");
        sb.AppendLine("<h3 style='margin-top:0; color:#334155;'>📊 ملخص الإحصائيات وبطاقة النتائج</h3>");
        sb.AppendLine("<div class='stats-grid'>");
        sb.AppendLine($"<div class='stat-item'><div>إجمالي الألواح المدققة</div><div class='stat-val'>{summary.TotalAudited}</div></div>");
        sb.AppendLine($"<div class='stat-item'><div>عدد الألواح المطابقة</div><div class='stat-val' style='color:#059669;'>{summary.MatchedCount}</div></div>");
        sb.AppendLine($"<div class='stat-item'><div>عدد الألواح غير المطابقة</div><div class='stat-val' style='color:#dc2626;'>{summary.MismatchedCount}</div></div>");
        sb.AppendLine($"<div class='stat-item'><div>نسبة دقة قارئ الـ EL</div><div class='stat-val acc'>{summary.AccuracyPercentage}%</div></div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</div>");

        // Audit Log Table
        sb.AppendLine("<h3>📋 جدول سجل القرارات والتعديلات</h3>");
        sb.AppendLine("<table>");
        sb.AppendLine("<thead>");
        sb.AppendLine("<tr>");
        sb.AppendLine("<th style='width: 5%;'>#</th>");
        sb.AppendLine("<th style='width: 25%;'>الرقم التسلسلي للوح</th>");
        sb.AppendLine("<th style='width: 15%;'>info مطابق للـ marked؟</th>");
        sb.AppendLine("<th style='width: 25%;'>بيانات info المقروءة</th>");
        sb.AppendLine("<th style='width: 30%;'>بيانات المصحح (التعديلات)</th>");
        sb.AppendLine("</tr>");
        sb.AppendLine("</thead>");
        sb.AppendLine("<tbody>");

        int index = 1;
        foreach (var rec in records)
        {
            string matchBadge = rec.IsMatched
                ? "<span class='badge-pass'>نعم (مطابق)</span>"
                : "<span class='badge-fail'>لا (غير مطابق)</span>";

            string elDefectsStr = rec.ElDefects.Count > 0 ? string.Join(", ", rec.ElDefects) : "لا توجد عيوب";
            string humanDefectsStr = rec.HumanCorrections.Count > 0 ? string.Join(", ", rec.HumanCorrections) : (rec.IsMatched ? "مطابق للمقروء" : "لا توجد عيوب");

            sb.AppendLine("<tr>");
            sb.AppendLine($"<td>{index++}</td>");
            sb.AppendLine($"<td><strong>{rec.SerialNumber}</strong></td>");
            sb.AppendLine($"<td>{matchBadge}</td>");
            sb.AppendLine($"<td>{elDefectsStr}</td>");
            sb.AppendLine($"<td>{humanDefectsStr}</td>");
            sb.AppendLine("</tr>");
        }

        sb.AppendLine("</tbody>");
        sb.AppendLine("</table>");

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return Encoding.UTF8.GetBytes(sb.ToString());
    }
}
