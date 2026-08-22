using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WinFIMLog
{
    internal static class ServiceInstaller
    {
        internal const string ServiceName = "WinFIMLog";
        private const string DefaultInstallFolderName = "WinFIMLog";
        private const string Description = "A File Integrity Monitoring service that keeps track of file changes in specified folders.";
        private const string DisplayName = "WinFIMLog";

        internal static bool TryHandleCommand(string[] args)
        {
            if (args.Length is 0)
            {
                return false;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "install":
                case "-i":
                case "--install":
                    Install(args.Skip(1).ToArray());
                    return true;

                case "uninstall":
                case "-u":
                case "--uninstall":
                    Uninstall(args.Skip(1).ToArray());
                    return true;

                case "help":
                case "-h":
                case "--help":
                case "/?":
                    WriteUsage();
                    return true;

                default:
                    return false;
            }
        }

        private static void CopyApplicationFiles(string sourceDirectory, string installDirectory, string sourceExecutable, string targetExecutable)
        {
            foreach (var file in Directory.EnumerateFiles(sourceDirectory))
            {
                var target = Path.Combine(installDirectory, Path.GetFileName(file));
                if (string.Equals(file, target, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                File.Copy(file, target, overwrite: true);
            }

            if (!File.Exists(targetExecutable))
            {
                File.Copy(sourceExecutable, targetExecutable, overwrite: true);
            }
        }

        private static string GetCurrentExecutablePath() =>
            Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine the current executable path.");

        private static string GetDefaultInstallDirectory()
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            return Path.Combine(programFiles, DefaultInstallFolderName);
        }

        private static string? GetOption(string[] args, string optionName)
        {
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], optionName, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    return args[i + 1];
                }
            }

            return null;
        }

        private static bool HasOption(string[] args, string optionName) =>
            args.Any(arg => string.Equals(arg, optionName, StringComparison.OrdinalIgnoreCase));

        private static void Install(string[] args)
        {
            var installDirectory = GetOption(args, "--install-dir") ?? GetDefaultInstallDirectory();
            var startService = HasOption(args, "--start");
            var sourceExecutable = GetCurrentExecutablePath();
            var targetExecutable = Path.Combine(installDirectory, Path.GetFileName(sourceExecutable));

            var serviceExists = ServiceExists();
            if (serviceExists)
            {
                RunSc("stop", ServiceName);
            }

            Directory.CreateDirectory(installDirectory);
            CopyApplicationFiles(Path.GetDirectoryName(sourceExecutable)!, installDirectory, sourceExecutable, targetExecutable);

            var quotedBinaryPath = Quote(targetExecutable);
            if (serviceExists)
            {
                RunSc("config", ServiceName, "binPath=", quotedBinaryPath, "start=", "auto", "DisplayName=", DisplayName);
            }
            else
            {
                RunSc("create", ServiceName, "binPath=", quotedBinaryPath, "start=", "auto", "DisplayName=", DisplayName);
            }

            RunSc("description", ServiceName, Description);
            RunSc("failure", ServiceName, "reset=", "86400", "actions=", "restart/60000/restart/60000/restart/60000");
            RunPowerShell(Path.Combine(installDirectory, "install-event-channels.ps1"));

            if (startService)
            {
                RunSc("start", ServiceName);
            }

            Console.WriteLine($"{ServiceName} installed to '{installDirectory}'.");
        }

        private static bool IsIgnorableStopFailure(string[] arguments, string output) =>
            arguments.Length > 0 &&
            string.Equals(arguments[0], "stop", StringComparison.OrdinalIgnoreCase) &&
            (output.Contains("1060", StringComparison.OrdinalIgnoreCase) || output.Contains("1062", StringComparison.OrdinalIgnoreCase));

        private static string Quote(string value) => $"\"{value}\"";

        private static void RunPowerShell(string script)
        {
            var result = RunProcessAsync("powershell.exe", "-NoProfile", "-NonInteractive",
                "-ExecutionPolicy", "Bypass", "-File", script).GetAwaiter().GetResult();
            if (result.ExitCode is not 0)
            {
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Event channel configuration failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Output}"));
            }
        }

        private static async Task<ProcessResult> RunProcessAsync(string fileName, params string[] arguments)
        {
            using var process = new Process();
            process.StartInfo.FileName = fileName;
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            foreach (var argument in arguments)
            {
                process.StartInfo.ArgumentList.Add(argument);
            }

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync() + await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, output);
        }

        private static void RunSc(params string[] arguments)
        {
            var result = RunProcessAsync("sc.exe", arguments).GetAwaiter().GetResult();
            if (result.ExitCode is not 0 && !IsIgnorableStopFailure(arguments, result.Output))
            {
                throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"sc.exe {string.Join(' ', arguments)} failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Output}"));
            }
        }

        private static bool ServiceExists()
        {
            var result = RunProcessAsync("sc.exe", "query", ServiceName).GetAwaiter().GetResult();
            return result.ExitCode is 0;
        }

        private static void Uninstall(string[] args)
        {
            var removeFiles = HasOption(args, "--remove-files");
            var installDirectory = GetOption(args, "--install-dir") ?? GetDefaultInstallDirectory();

            if (ServiceExists())
            {
                RunSc("stop", ServiceName);
                var removalScript = Path.Combine(installDirectory, "uninstall-event-channels.ps1");
                if (File.Exists(removalScript))
                {
                    RunPowerShell(removalScript);
                }

                RunSc("delete", ServiceName);
            }

            if (removeFiles && Directory.Exists(installDirectory))
            {
                Directory.Delete(installDirectory, recursive: true);
            }

            Console.WriteLine($"{ServiceName} was removed.");
        }

        private static void WriteUsage()
        {
            Console.WriteLine("WinFIMLog self-installer");
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  WinFIMLog.exe install [--install-dir <path>] [--start]");
            Console.WriteLine("  WinFIMLog.exe uninstall [--install-dir <path>] [--remove-files]");
            Console.WriteLine();
            Console.WriteLine("Aliases:");
            Console.WriteLine("  -i, --install      Install or update the Windows Service");
            Console.WriteLine("  -u, --uninstall    Stop and remove the Windows Service");
        }

        private sealed record ProcessResult(int ExitCode, string Output);
    }
}
