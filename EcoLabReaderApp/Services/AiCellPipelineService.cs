using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace EcoLabReaderApp.Services
{
    public class AiCellPipelineService
    {
        private readonly FileRestructurerService _restructurer;
        private readonly ElParserService _parser;

        public AiCellPipelineService(FileRestructurerService restructurer, ElParserService parser)
        {
            _restructurer = restructurer;
            _parser = parser;
        }

        public AiCellPipelineResult RunPipeline()
        {
            // Step 1: Run panel restructuring & model partitioning first
            _restructurer.RunFullRestructuringAndPartitioning(_parser);

            string restructuredPath = _restructurer.RestructuredPath;
            string baseDir = Path.GetFullPath(Path.Combine(restructuredPath, ".."));
            string aiCellPath = Path.Combine(baseDir, "AICell");

            string scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Services", "cell_cropper_pipeline.py");
            if (!File.Exists(scriptPath))
            {
                scriptPath = Path.Combine(Directory.GetCurrentDirectory(), "Services", "cell_cropper_pipeline.py");
            }

            if (!File.Exists(scriptPath))
            {
                return new AiCellPipelineResult
                {
                    Success = false,
                    Message = $"لم يتم العثور على سكربت التقطيع الذكي: {scriptPath}"
                };
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"\"{scriptPath}\" \"{restructuredPath}\" \"{aiCellPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return new AiCellPipelineResult
                    {
                        Success = false,
                        Message = "فشل بدء عملية Python المخصصة لتقطيع الخلايا."
                    };
                }

                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                {
                    return new AiCellPipelineResult
                    {
                        Success = false,
                        Message = $"خطأ أثناء تشغيل سكربت Python: {error}"
                    };
                }

                try
                {
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<AiCellPipelineResult>(output, options);
                    return result ?? new AiCellPipelineResult { Success = true, Message = "تم التقطيع بنجاح." };
                }
                catch
                {
                    return new AiCellPipelineResult
                    {
                        Success = true,
                        Message = string.IsNullOrEmpty(output) ? "تمت المعالجة بنجاح." : output
                    };
                }
            }
        }
    }

    public class AiCellPipelineResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int TotalPanels { get; set; }
        public int TotalGoodCells { get; set; }
        public int TotalBadCells { get; set; }
    }
}
