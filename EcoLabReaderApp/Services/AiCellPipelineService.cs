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

            string pythonExe = FindPythonExecutable();

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
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
                    string combinedErr = (error + " " + output).Trim();
                    if (combinedErr.Contains("was not found") || combinedErr.Contains("Microsoft Store"))
                    {
                        return new AiCellPipelineResult
                        {
                            Success = false,
                            Message = "لم يتم العثور على محرك Python مثبت على جهاز Windows هذا. يرجى تثبيت Python (أو التأكد من إضافة Python إلى متغيرات النظام PATH) ثم إعادة المحاولة."
                        };
                    }

                    return new AiCellPipelineResult
                    {
                        Success = false,
                        Message = $"خطأ أثناء تشغيل سكربت Python: {combinedErr}"
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

        private string FindPythonExecutable()
        {
            string[] candidates = OperatingSystem.IsWindows()
                ? new[] { "py", "python", "python3" }
                : new[] { "python3", "python" };

            foreach (var cmd in candidates)
            {
                try
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = cmd,
                        Arguments = "--version",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    using (var proc = Process.Start(psi))
                    {
                        if (proc != null)
                        {
                            string stdout = proc.StandardOutput.ReadToEnd();
                            string stderr = proc.StandardError.ReadToEnd();
                            proc.WaitForExit();

                            string combined = (stdout + stderr).ToLower();
                            if (proc.ExitCode == 0 && combined.Contains("python") && !combined.Contains("was not found"))
                            {
                                return cmd;
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore and try next candidate
                }
            }

            // Search Windows specific AppData paths if still not found
            if (OperatingSystem.IsWindows())
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                string programsPath = Path.Combine(localAppData, "Programs", "Python");
                if (Directory.Exists(programsPath))
                {
                    foreach (var dir in Directory.GetDirectories(programsPath, "Python3*"))
                    {
                        string exe = Path.Combine(dir, "python.exe");
                        if (File.Exists(exe)) return exe;
                    }
                }
            }

            return OperatingSystem.IsWindows() ? "python" : "python3";
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
