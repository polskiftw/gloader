using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace GLoader
{
    internal sealed class X64RuntimeBuilder
    {
        private readonly string _loaderDirectory;
        private readonly string _logsDirectory;

        public X64RuntimeBuilder(string loaderDirectory, string logsDirectory)
        {
            _loaderDirectory = Path.GetFullPath(loaderDirectory ?? throw new ArgumentNullException(nameof(loaderDirectory)));
            _logsDirectory = Path.GetFullPath(logsDirectory ?? throw new ArgumentNullException(nameof(logsDirectory)));
        }

        public string RuntimeDirectory => Path.Combine(_loaderDirectory, "gdeps", TargetLocator.X64RuntimeDirectoryName);
        public string ManagedTarget => Path.Combine(RuntimeDirectory, "TerrariaRelease.dll");
        public string ScriptPath => Path.Combine(_loaderDirectory, "gdeps", "tools", "x64-runtime", "Build-X64Runtime.ps1");
        public string LogPath => Path.Combine(_logsDirectory, "x64-runtime-build.log");
        public bool IsReady => File.Exists(ManagedTarget);
        public bool CanBuild => File.Exists(ScriptPath) && File.Exists(Path.Combine(_loaderDirectory, "Terraria.exe"));

        public async Task<RuntimeBuildResult> BuildAsync()
        {
            if (!File.Exists(Path.Combine(_loaderDirectory, "Terraria.exe")))
            {
                throw new FileNotFoundException(
                    "Terraria.exe must be beside gloader.exe before the private x64 runtime can be built.",
                    Path.Combine(_loaderDirectory, "Terraria.exe"));
            }

            if (!File.Exists(ScriptPath))
            {
                throw new FileNotFoundException(
                    "The gloader x64 runtime builder is missing.",
                    ScriptPath);
            }

            Directory.CreateDirectory(_logsDirectory);

            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = _loaderDirectory
            };
            startInfo.ArgumentList.Add("-NoLogo");
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(ScriptPath);
            startInfo.ArgumentList.Add("-TerrariaDirectory");
            startInfo.ArgumentList.Add(_loaderDirectory);
            startInfo.ArgumentList.Add("-OutputDirectory");
            startInfo.ArgumentList.Add(RuntimeDirectory);

            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                throw new InvalidOperationException("Could not start the x64 Terraria runtime builder.");

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync();

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            var log = new StringBuilder();
            log.AppendLine("gloader x64 runtime builder");
            log.AppendLine("UTC: " + DateTime.UtcNow.ToString("O"));
            log.AppendLine("Exit code: " + process.ExitCode);
            log.AppendLine();
            log.Append(stdout);
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                log.AppendLine();
                log.AppendLine("--- stderr ---");
                log.Append(stderr);
            }

            File.WriteAllText(LogPath, log.ToString());

            return new RuntimeBuildResult(
                process.ExitCode,
                IsReady,
                LogPath,
                LastMeaningfulLine(stderr, stdout));
        }

        private static string LastMeaningfulLine(params string[] textBlocks)
        {
            foreach (var block in textBlocks)
            {
                if (string.IsNullOrWhiteSpace(block))
                    continue;

                var lines = block.Replace("\r", string.Empty)
                    .Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (var i = lines.Length - 1; i >= 0; i--)
                {
                    var line = lines[i].Trim();
                    if (!string.IsNullOrWhiteSpace(line))
                        return line;
                }
            }

            return null;
        }
    }

    internal sealed class RuntimeBuildResult
    {
        public RuntimeBuildResult(int exitCode, bool runtimeReady, string logPath, string lastMessage)
        {
            ExitCode = exitCode;
            RuntimeReady = runtimeReady;
            LogPath = logPath;
            LastMessage = lastMessage;
        }

        public int ExitCode { get; }
        public bool RuntimeReady { get; }
        public string LogPath { get; }
        public string LastMessage { get; }
        public bool Success => ExitCode == 0 && RuntimeReady;
    }
}
