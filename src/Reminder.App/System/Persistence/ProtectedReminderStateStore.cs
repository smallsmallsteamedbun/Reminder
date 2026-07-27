using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Reminder.App.Logic.State;

namespace Reminder.App.SystemModule.Persistence;

public sealed class ProtectedReminderStateStore
{
    private const int MaximumProtectedFileBytes = 16 * 1024 * 1024;
    private static readonly byte[] OptionalEntropy =
        SHA256.HashData(
            Encoding.UTF8.GetBytes(
                "Reminder.State.CurrentUser.v1"));
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        MaxDepth = 32,
        PropertyNameCaseInsensitive = false,
        WriteIndented = false
    };

    private readonly object _fileGate = new();
    private readonly string _dataDirectory;
    private readonly string _statePath;
    private readonly string _backupPath;
    private readonly string _temporaryPath;

    public ProtectedReminderStateStore(string? applicationDirectory = null)
    {
        var baseDirectory = Path.GetFullPath(
            applicationDirectory ?? AppContext.BaseDirectory);
        _dataDirectory = Path.Combine(baseDirectory, "Data");
        _statePath = Path.Combine(_dataDirectory, "state.dat");
        _backupPath = Path.Combine(_dataDirectory, "state.bak");
        _temporaryPath = Path.Combine(_dataDirectory, "state.tmp");
    }

    public string DataDirectory => _dataDirectory;

    public ReminderStateLoadResult Load()
    {
        lock (_fileGate)
        {
            var primaryExists = File.Exists(_statePath);
            var backupExists = File.Exists(_backupPath);
            if (!primaryExists && !backupExists)
            {
                return new ReminderStateLoadResult
                {
                    Status = ReminderStateLoadStatus.NoData,
                    State = null,
                    ErrorMessage = string.Empty
                };
            }

            string primaryError = "正式状态文件不存在。";
            ReminderEngineState primaryState = null!;
            if (primaryExists &&
                TryLoadFile(
                    _statePath,
                    out primaryState,
                    out primaryError))
            {
                return new ReminderStateLoadResult
                {
                    Status = ReminderStateLoadStatus.LoadedPrimary,
                    State = primaryState,
                    ErrorMessage = string.Empty
                };
            }

            string backupError = "备份状态文件不存在。";
            ReminderEngineState backupState = null!;
            if (backupExists &&
                TryLoadFile(
                    _backupPath,
                    out backupState,
                    out backupError))
            {
                TryPromoteBackup();
                return new ReminderStateLoadResult
                {
                    Status = ReminderStateLoadStatus.LoadedBackup,
                    State = backupState,
                    ErrorMessage = string.Empty
                };
            }

            return new ReminderStateLoadResult
            {
                Status = ReminderStateLoadStatus.RecoveryFailed,
                State = null,
                ErrorMessage =
                    $"正式状态：{primaryError} 备份状态：{backupError}"
            };
        }
    }

    public ReminderStateLoadResult LoadBackup()
    {
        lock (_fileGate)
        {
            var backupError = "备份状态文件不存在。";
            ReminderEngineState backupState = null!;
            if (File.Exists(_backupPath) &&
                TryLoadFile(
                    _backupPath,
                    out backupState,
                    out backupError))
            {
                TryPromoteBackup();
                return new ReminderStateLoadResult
                {
                    Status = ReminderStateLoadStatus.LoadedBackup,
                    State = backupState,
                    ErrorMessage = string.Empty
                };
            }

            return new ReminderStateLoadResult
            {
                Status = ReminderStateLoadStatus.RecoveryFailed,
                State = null,
                ErrorMessage = File.Exists(_backupPath)
                    ? backupError
                    : "备份状态文件不存在。"
            };
        }
    }

    public ReminderStateSaveResult Save(ReminderEngineState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        lock (_fileGate)
        {
            try
            {
                Directory.CreateDirectory(_dataDirectory);
                var document = ReminderStateMapper.ToDocument(state);
                var jsonBytes =
                    JsonSerializer.SerializeToUtf8Bytes(
                        document,
                        JsonOptions);
                var protectedBytes = ProtectedData.Protect(
                    jsonBytes,
                    OptionalEntropy,
                    DataProtectionScope.CurrentUser);

                WriteTemporaryFile(protectedBytes);
                if (File.Exists(_statePath))
                {
                    File.Replace(
                        _temporaryPath,
                        _statePath,
                        _backupPath,
                        ignoreMetadataErrors: true);
                }
                else
                {
                    File.Move(_temporaryPath, _statePath);
                }

                return ReminderStateSaveResult.Success;
            }
            catch (Exception exception) when (
                exception is IOException or
                UnauthorizedAccessException or
                CryptographicException or
                JsonException or
                NotSupportedException)
            {
                TryDeleteTemporaryFile();
                return new ReminderStateSaveResult
                {
                    IsSuccess = false,
                    ErrorMessage = exception.Message
                };
            }
        }
    }

    private static bool TryDeserialize(
        byte[] protectedBytes,
        out ReminderEngineState state,
        out string errorMessage)
    {
        state = null!;
        errorMessage = string.Empty;
        try
        {
            var jsonBytes = ProtectedData.Unprotect(
                protectedBytes,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
            var document =
                JsonSerializer.Deserialize<ReminderStateDocument>(
                    jsonBytes,
                    JsonOptions);
            return ReminderStateMapper.TryToEngineState(
                document,
                out state,
                out errorMessage);
        }
        catch (Exception exception) when (
            exception is CryptographicException or
            JsonException or
            NotSupportedException or
            ArgumentException)
        {
            errorMessage = exception.Message;
            return false;
        }
    }

    private static byte[] ReadProtectedFile(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16_384,
            FileOptions.SequentialScan);
        if (stream.Length is <= 0 or > MaximumProtectedFileBytes)
        {
            throw new InvalidDataException(
                "状态文件大小无效。");
        }

        var length = checked((int)stream.Length);
        var bytes = new byte[length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    private static bool TryReadFile(
        string path,
        out byte[] bytes,
        out string errorMessage)
    {
        try
        {
            bytes = ReadProtectedFile(path);
            errorMessage = string.Empty;
            return true;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            NotSupportedException)
        {
            bytes = [];
            errorMessage = exception.Message;
            return false;
        }
    }

    private static bool TryLoadFile(
        string path,
        out ReminderEngineState state,
        out string errorMessage)
    {
        state = null!;
        if (!TryReadFile(path, out var bytes, out errorMessage))
        {
            return false;
        }

        return TryDeserialize(bytes, out state, out errorMessage);
    }

    private void WriteTemporaryFile(byte[] bytes)
    {
        using var stream = new FileStream(
            _temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16_384,
            FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);
    }

    private void TryPromoteBackup()
    {
        try
        {
            Directory.CreateDirectory(_dataDirectory);
            var backupBytes = ReadProtectedFile(_backupPath);
            WriteTemporaryFile(backupBytes);
            File.Move(
                _temporaryPath,
                _statePath,
                overwrite: true);
        }
        catch
        {
            TryDeleteTemporaryFile();
            // The valid backup remains available. Promotion is best-effort and
            // does not invalidate the successfully recovered in-memory state.
        }
    }

    private void TryDeleteTemporaryFile()
    {
        try
        {
            if (File.Exists(_temporaryPath))
            {
                File.Delete(_temporaryPath);
            }
        }
        catch
        {
            // A stale temporary file is never loaded and may be replaced by a
            // later save, so cleanup failure is non-fatal.
        }
    }
}
