using Whisper.net.Ggml;
using TerminalHost.Core.Domain;
using TerminalHost.Core.Interfaces;

namespace TerminalHost.Core.Services;

/// <summary>
/// Manages Whisper GGML model downloads and caching.
/// Models are stored in the platform-specific application data directory under models/whisper/.
/// </summary>
public class WhisperModelManager
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IToastService _toastService;
    private readonly string _modelsDir;

    public WhisperModelManager(ISystemInfoService systemInfo, IToastService toastService)
    {
        _systemInfo = systemInfo;
        _toastService = toastService;

        var appData = systemInfo.GetApplicationDataPath();
        _modelsDir = Path.Combine(appData, "models", "whisper");
    }

    /// <summary>
    /// Get the file path for a given model size.
    /// </summary>
    public string GetModelPath(WhisperModelSize size)
    {
        var fileName = $"ggml-{MapToGgmlType(size).ToString().ToLowerInvariant()}.bin";
        return Path.Combine(_modelsDir, fileName);
    }

    /// <summary>
    /// Check if the model file exists on disk.
    /// </summary>
    public bool IsModelDownloaded(WhisperModelSize size)
    {
        var path = GetModelPath(size);
        return File.Exists(path);
    }

    /// <summary>
    /// Get a human-readable status string for the model (e.g., "Downloaded (466 MB)" or "Not downloaded").
    /// </summary>
    public string GetModelStatus(WhisperModelSize size)
    {
        var path = GetModelPath(size);
        if (!File.Exists(path))
            return "Not downloaded";

        var fileInfo = new FileInfo(path);
        var sizeMb = fileInfo.Length / (1024.0 * 1024.0);
        return sizeMb >= 1024
            ? $"Downloaded ({sizeMb / 1024.0:F1} GB)"
            : $"Downloaded ({sizeMb:F0} MB)";
    }

    /// <summary>
    /// Ensure the model is downloaded. Shows a progress toast during download.
    /// Returns the model file path, or null if download failed.
    /// </summary>
    public async Task<string?> EnsureModelAsync(WhisperModelSize size, CancellationToken cancellationToken = default)
    {
        var path = GetModelPath(size);
        if (File.Exists(path))
            return path;

        Directory.CreateDirectory(_modelsDir);

        var ggmlType = MapToGgmlType(size);
        var sizeLabel = GetSizeLabel(size);
        var toast = _toastService.ShowProgress($"Downloading Whisper {size} model ({sizeLabel})...");

        var tempPath = path + ".tmp";
        try
        {
            using var stream = await WhisperGgmlDownloader.Default.GetGgmlModelAsync(ggmlType);
            using var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await stream.CopyToAsync(fileStream, cancellationToken);
            await fileStream.FlushAsync(cancellationToken);
            fileStream.Close();

            // Atomic rename
            File.Move(tempPath, path, overwrite: true);

            toast.Complete($"Whisper {size} model ready");
            return path;
        }
        catch (OperationCanceledException)
        {
            CleanupTempFile(tempPath);
            toast.Fail("Model download cancelled");
            return null;
        }
        catch (Exception ex)
        {
            CleanupTempFile(tempPath);
            toast.Fail($"Model download failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Delete a downloaded model file.
    /// </summary>
    public void DeleteModel(WhisperModelSize size)
    {
        var path = GetModelPath(size);
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // Model may be in use
            }
        }
    }

    private static void CleanupTempFile(string tempPath)
    {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
    }

    private static GgmlType MapToGgmlType(WhisperModelSize size) => size switch
    {
        WhisperModelSize.Tiny => GgmlType.Tiny,
        WhisperModelSize.Base => GgmlType.Base,
        WhisperModelSize.Small => GgmlType.Small,
        WhisperModelSize.Medium => GgmlType.Medium,
        WhisperModelSize.LargeV3 => GgmlType.LargeV3,
        _ => GgmlType.Small
    };

    /// <summary>
    /// Get the approximate download size label for a model.
    /// </summary>
    public static string GetSizeLabel(WhisperModelSize size) => size switch
    {
        WhisperModelSize.Tiny => "~75 MB",
        WhisperModelSize.Base => "~142 MB",
        WhisperModelSize.Small => "~466 MB",
        WhisperModelSize.Medium => "~1.5 GB",
        WhisperModelSize.LargeV3 => "~3 GB",
        _ => ""
    };
}
