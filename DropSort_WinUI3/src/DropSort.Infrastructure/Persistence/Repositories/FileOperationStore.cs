using DropSort.Application.Repositories;
using DropSort.Domain.Core.Operations;
using Microsoft.Data.Sqlite;

namespace DropSort.Infrastructure.Persistence.Repositories;

public sealed class FileOperationStore : IFileOperationStore, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly bool _ownsConnection;
    private readonly Func<DateTimeOffset> _clock;

    private static readonly IReadOnlyDictionary<OperationState, HashSet<OperationState>> AllowedTransitions =
        new Dictionary<OperationState, HashSet<OperationState>>
        {
            [OperationState.Planned] = [OperationState.Validated, OperationState.Failed],
            [OperationState.Validated] = [OperationState.Executing, OperationState.Failed],
            [OperationState.Executing] = [OperationState.FsVerified, OperationState.Failed, OperationState.RecoveryRequired],
            [OperationState.FsVerified] = [OperationState.Committed, OperationState.RecoveryRequired],
            [OperationState.RecoveryRequired] = [OperationState.FsVerified, OperationState.Failed],
            [OperationState.Committed] = [],
            [OperationState.Failed] = []
        };

    public FileOperationStore(SqliteConnection connection, Func<DateTimeOffset>? clock = null)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        if (_connection.State != System.Data.ConnectionState.Open) _connection.Open();
        using var command = _connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA synchronous = FULL;";
        command.ExecuteNonQuery();
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public FileOperationStore(string connectionString, Func<DateTimeOffset>? clock = null)
    {
        _connection = SqliteConnections.Open(connectionString);
        _ownsConnection = true;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public FileOperationRecord? GetById(string id) => GetById(id, null);

    public IReadOnlyList<FileOperationRecord> ListHistory(int limit = 50, int offset = 0)
    {
        if (limit <= 0) throw new ArgumentOutOfRangeException(nameof(limit));
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM file_operations ORDER BY datetime(created_at) DESC, id DESC LIMIT $limit OFFSET $offset;";
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        return ReadMany(command);
    }

    public IReadOnlyList<FileOperationRecord> ListNonterminal()
    {
        using var command = _connection.CreateCommand();
        command.CommandText = "SELECT * FROM file_operations WHERE state NOT IN ('COMMITTED', 'FAILED') ORDER BY datetime(created_at), id;";
        return ReadMany(command);
    }

    public FileOperationRecord Create(FileOperationPlan plan, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(plan);
        using var transaction = _connection.BeginTransaction();
        if (plan.ReversesOperationId is not null)
        {
            using var check = _connection.CreateCommand();
            check.Transaction = transaction;
            check.CommandText = "SELECT id FROM file_operations WHERE reverses_operation_id = $reversed LIMIT 1;";
            check.Parameters.AddWithValue("$reversed", plan.ReversesOperationId);
            if (check.ExecuteScalar() is not null)
                throw new InvalidOperationException($"Operation {plan.ReversesOperationId} already has a reverse journal.");
        }

        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO file_operations (
                id, operation_type, source_path, destination_path, state,
                media_file_id, reverses_operation_id, created_at, updated_at
            ) VALUES (
                $id, $type, $source, $destination, 'PLANNED',
                $media, $reverses, $created, $updated
            );
            """;
        command.Parameters.AddWithValue("$id", plan.OperationId);
        command.Parameters.AddWithValue("$type", ToText(plan.OperationType));
        command.Parameters.AddWithValue("$source", plan.Source);
        command.Parameters.AddWithValue("$destination", plan.Destination);
        command.Parameters.AddWithValue("$media", (object?)plan.MediaFileId ?? DBNull.Value);
        command.Parameters.AddWithValue("$reverses", (object?)plan.ReversesOperationId ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", now.ToString("O"));
        command.Parameters.AddWithValue("$updated", now.ToString("O"));
        command.ExecuteNonQuery();
        transaction.Commit();
        return GetById(plan.OperationId)!;
    }

    public FileOperationRecord Transition(string id, OperationState newState, OperationUpdate? update = null)
    {
        using var transaction = _connection.BeginTransaction();
        var result = Transition(id, newState, update ?? new OperationUpdate(), transaction);
        transaction.Commit();
        return result;
    }

    public FileOperationRecord CommitVerified(string id)
    {
        using var transaction = _connection.BeginTransaction();
        var current = GetById(id, transaction) ?? throw new KeyNotFoundException($"Operation {id} was not found.");
        if (current.State != OperationState.FsVerified)
            throw new InvalidOperationException($"Expected FsVerified, got {current.State}.");
        if (current.MediaFileId is not null)
        {
            using var updatePath = _connection.CreateCommand();
            updatePath.Transaction = transaction;
            updatePath.CommandText = """
                UPDATE media_files
                   SET current_path = $path, path_key = $key, status = 'PRESENT', last_seen_at = $updated
                 WHERE id = $id;
                """;
            updatePath.Parameters.AddWithValue("$path", current.Destination);
            updatePath.Parameters.AddWithValue("$key", MediaFileRepository.PathKey(current.Destination));
            updatePath.Parameters.AddWithValue("$updated", _clock().ToString("O"));
            updatePath.Parameters.AddWithValue("$id", current.MediaFileId.Value);
            if (updatePath.ExecuteNonQuery() != 1)
                throw new KeyNotFoundException($"Media file {current.MediaFileId.Value} was not found.");
        }
        var committed = Transition(id, OperationState.Committed, new OperationUpdate(), transaction);
        transaction.Commit();
        return committed;
    }

    public void Dispose()
    {
        if (_ownsConnection) _connection.Dispose();
    }

    private FileOperationRecord Transition(
        string id,
        OperationState newState,
        OperationUpdate update,
        SqliteTransaction transaction)
    {
        var current = GetById(id, transaction) ?? throw new KeyNotFoundException($"Operation {id} was not found.");
        if (!AllowedTransitions[current.State].Contains(newState))
            throw new InvalidOperationException($"Invalid transition: {current.State} -> {newState}.");

        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE file_operations
               SET state = $state,
                   source_size = $sourceSize, source_mtime_ns = $sourceMtime,
                   source_dev = $sourceDev, source_ino = $sourceIno,
                   destination_size = $destinationSize, destination_mtime_ns = $destinationMtime,
                   destination_dev = $destinationDev, destination_ino = $destinationIno,
                   destination_sha256 = $sha, strategy = $strategy,
                   error_code = $errorCode, error_message = $errorMessage, updated_at = $updated
             WHERE id = $id AND state = $oldState;
            """;
        command.Parameters.AddWithValue("$state", ToText(newState));
        command.Parameters.AddWithValue("$sourceSize", Db(update.SourceSize ?? current.SourceSize));
        command.Parameters.AddWithValue("$sourceMtime", Db(update.SourceMTimeNs ?? current.SourceMTimeNs));
        command.Parameters.AddWithValue("$sourceDev", DbText(update.SourceDev ?? current.SourceDev));
        command.Parameters.AddWithValue("$sourceIno", DbText(update.SourceIno ?? current.SourceIno));
        command.Parameters.AddWithValue("$destinationSize", Db(update.DestinationSize ?? current.DestinationSize));
        command.Parameters.AddWithValue("$destinationMtime", Db(update.DestinationMTimeNs ?? current.DestinationMTimeNs));
        command.Parameters.AddWithValue("$destinationDev", DbText(update.DestinationDev ?? current.DestinationDev));
        command.Parameters.AddWithValue("$destinationIno", DbText(update.DestinationIno ?? current.DestinationIno));
        command.Parameters.AddWithValue("$sha", Db(update.DestinationSha256 ?? current.DestinationSha256));
        command.Parameters.AddWithValue("$strategy", Db(update.Strategy ?? current.Strategy));
        command.Parameters.AddWithValue("$errorCode", Db(update.ErrorCode));
        command.Parameters.AddWithValue("$errorMessage", Db(update.ErrorMessage));
        command.Parameters.AddWithValue("$updated", _clock().ToString("O"));
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$oldState", ToText(current.State));
        if (command.ExecuteNonQuery() != 1)
            throw new InvalidOperationException($"Concurrent state change for {id}.");
        return GetById(id, transaction)!;
    }

    private FileOperationRecord? GetById(string id, SqliteTransaction? transaction)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Operation ID is required.", nameof(id));
        using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM file_operations WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Map(reader) : null;
    }

    private static IReadOnlyList<FileOperationRecord> ReadMany(SqliteCommand command)
    {
        var records = new List<FileOperationRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read()) records.Add(Map(reader));
        return records;
    }

    private static FileOperationRecord Map(SqliteDataReader reader) => new(
        reader.GetString(0),
        ParseOperationType(reader.GetString(1)),
        reader.GetString(2),
        reader.GetString(3),
        ParseOperationState(reader.GetString(4)),
        reader.IsDBNull(5) ? null : reader.GetInt32(5),
        reader.IsDBNull(6) ? null : reader.GetString(6),
        reader.IsDBNull(7) ? null : reader.GetInt64(7),
        reader.IsDBNull(8) ? null : reader.GetInt64(8),
        reader.IsDBNull(9) ? null : ulong.Parse(reader.GetString(9), System.Globalization.CultureInfo.InvariantCulture),
        reader.IsDBNull(10) ? null : ulong.Parse(reader.GetString(10), System.Globalization.CultureInfo.InvariantCulture),
        reader.IsDBNull(11) ? null : reader.GetInt64(11),
        reader.IsDBNull(12) ? null : reader.GetInt64(12),
        reader.IsDBNull(13) ? null : ulong.Parse(reader.GetString(13), System.Globalization.CultureInfo.InvariantCulture),
        reader.IsDBNull(14) ? null : ulong.Parse(reader.GetString(14), System.Globalization.CultureInfo.InvariantCulture),
        reader.IsDBNull(15) ? null : reader.GetString(15),
        reader.IsDBNull(16) ? null : reader.GetString(16),
        reader.IsDBNull(17) ? null : reader.GetString(17),
        reader.IsDBNull(18) ? null : reader.GetString(18),
        MovieRepository.ParseTimestamp(reader.GetString(19)),
        MovieRepository.ParseTimestamp(reader.GetString(20)));

    private static object Db(object? value) => value ?? DBNull.Value;
    private static object DbText(ulong? value) =>
        (object?)value?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? DBNull.Value;

    private static string ToText(OperationType type) => type switch
    {
        OperationType.Move => "MOVE",
        OperationType.Rename => "RENAME",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    private static OperationType ParseOperationType(string value) => value switch
    {
        "MOVE" => OperationType.Move,
        "RENAME" => OperationType.Rename,
        _ => throw new InvalidDataException($"Unknown operation type '{value}'.")
    };

    private static string ToText(OperationState state) => state switch
    {
        OperationState.Planned => "PLANNED",
        OperationState.Validated => "VALIDATED",
        OperationState.Executing => "EXECUTING",
        OperationState.FsVerified => "FS_VERIFIED",
        OperationState.Committed => "COMMITTED",
        OperationState.Failed => "FAILED",
        OperationState.RecoveryRequired => "RECOVERY_REQUIRED",
        _ => throw new ArgumentOutOfRangeException(nameof(state))
    };

    private static OperationState ParseOperationState(string value) => value switch
    {
        "PLANNED" => OperationState.Planned,
        "VALIDATED" => OperationState.Validated,
        "EXECUTING" => OperationState.Executing,
        "FS_VERIFIED" => OperationState.FsVerified,
        "COMMITTED" => OperationState.Committed,
        "FAILED" => OperationState.Failed,
        "RECOVERY_REQUIRED" => OperationState.RecoveryRequired,
        _ => throw new InvalidDataException($"Unknown operation state '{value}'.")
    };
}
