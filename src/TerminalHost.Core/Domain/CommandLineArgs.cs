using System.IO;

namespace TerminalHost.Core.Domain;

public class CommandLineArgs
{
    public string? ProfileId { get; set; }
    public string? Command { get; set; }
    public string? WorkingDir { get; set; }
    public bool IsSetupMode { get; set; }
    public bool DisableSingleInstance { get; set; }
    public string? UserDataDir { get; set; }

    public static CommandLineArgs Parse(string[] args)
    {
        var result = new CommandLineArgs();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            // Handle named arguments
            switch (arg.ToLowerInvariant())
            {
                case "/setup":
                case "-setup":
                case "--setup":
                    result.IsSetupMode = true;
                    continue;

                case "--disable-single-instance":
                case "-multi": // Short alias for testing convenience
                    result.DisableSingleInstance = true;
                    continue;

                case "--user-data-dir":
                case "-data":
                    if (i + 1 < args.Length)
                        result.UserDataDir = ResolveDirectory(args[++i]);
                    continue;

                case "--profile":
                case "-p":
                    if (i + 1 < args.Length)
                        result.ProfileId = args[++i];
                    continue;

                case "--command":
                case "-c":
                    if (i + 1 < args.Length)
                        result.Command = args[++i];
                    continue;

                case "--workdir":
                case "-w":
                    if (i + 1 < args.Length)
                        result.WorkingDir = ResolveDirectory(args[++i]);
                    continue;
            }

            // Handle positional argument (first non-flag argument is treated as directory)
            if (!arg.StartsWith("-") && string.IsNullOrEmpty(result.WorkingDir))
            {
                result.WorkingDir = ResolveDirectory(arg);
            }
        }

        return result;
    }

    private static string ResolveDirectory(string path)
    {
        // Handle "." and relative paths
        if (path == ".")
        {
            return Directory.GetCurrentDirectory();
        }

        // Check if it's a relative path and make it absolute
        if (!Path.IsPathRooted(path))
        {
            return Path.GetFullPath(path);
        }

        return path;
    }

    public bool HasValidRequest()
    {
        return !string.IsNullOrEmpty(WorkingDir) || IsSetupMode;
    }
}
