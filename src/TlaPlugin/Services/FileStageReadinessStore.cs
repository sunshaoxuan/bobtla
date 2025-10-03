using System;
using System.Globalization;
using System.IO;

namespace TlaPlugin.Services;

/// <summary>
/// 通过文件持久化阶段冒烟成功时间的默认实现。
/// </summary>
public class FileStageReadinessStore : IStageReadinessStore
{
    private static readonly string DefaultFilePath = Path.Combine(AppContext.BaseDirectory, "App_Data", "stage-readiness.txt");
    private readonly string _filePath;
    private readonly object _sync = new();

    public FileStageReadinessStore()
        : this(DefaultFilePath)
    {
    }

    public FileStageReadinessStore(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("文件路径不可为空。", nameof(filePath));
        }

        _filePath = filePath;
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
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
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
            catch (IOException)
            {
                // 忽略文件写入异常以避免影响主流程。
            }
            catch (UnauthorizedAccessException)
            {
                // 忽略文件写入异常以避免影响主流程。
            }
        }
    }
}
