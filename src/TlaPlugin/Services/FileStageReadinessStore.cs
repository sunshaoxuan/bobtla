using System;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace TlaPlugin.Services;

/// <summary>
/// 通过文件持久化阶段冒烟成功时间的默认实现。
/// </summary>
public class FileStageReadinessStore : IStageReadinessStore
{
    private static readonly string DefaultFilePath = Path.Combine(AppContext.BaseDirectory, "App_Data", "stage-readiness.txt");
    private readonly string _filePath;
    private readonly ILogger<FileStageReadinessStore> _logger;
    private readonly object _sync = new();

    public FileStageReadinessStore()
        : this(DefaultFilePath, null)
    {
    }

    public FileStageReadinessStore(string filePath, ILogger<FileStageReadinessStore>? logger = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("文件路径不可为空。", nameof(filePath));
        }

        _filePath = filePath;
        _logger = logger ?? NullLogger<FileStageReadinessStore>.Instance;
    }

    public DateTimeOffset? ReadLastSuccess()
    {
        lock (_sync)
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return null;
                }

                var content = File.ReadAllText(_filePath).Trim();
                if (string.IsNullOrWhiteSpace(content))
                {
                    return null;
                }

                if (DateTimeOffset.TryParse(content, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var value))
                {
                    return value;
                }
            }
            catch (IOException ex)
            {
                _logger.LogError(ex, "读取阶段就绪时间文件 '{FilePath}' 失败。", _filePath);
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogError(ex, "读取阶段就绪时间文件 '{FilePath}' 失败。", _filePath);
                return null;
            }
        }

        return null;
    }

    public void WriteLastSuccess(DateTimeOffset timestamp)
    {
        lock (_sync)
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_filePath, timestamp.ToString("O", CultureInfo.InvariantCulture));
            }
            catch (IOException ex)
            {
                _logger.LogWarning(ex, "写入阶段就绪时间文件 '{FilePath}' 失败。", _filePath);
                // 忽略文件写入异常以避免影响主流程。
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "写入阶段就绪时间文件 '{FilePath}' 失败。", _filePath);
                // 忽略文件写入异常以避免影响主流程。
            }
        }
    }
}
