using System;
using Microsoft.Data.Sqlite;
using DropSort.Application.Repositories;

namespace DropSort.Infrastructure.Persistence.Repositories;

public class LibraryMaintenanceRepository : ILibraryMaintenanceRepository
{
    private readonly string _connectionString;

    public LibraryMaintenanceRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    public (int Movies, int MediaFiles, int MetadataEntries) ClearCatalog()
    {
        using var connection = SqliteConnections.Open(_connectionString);
        using var cmd = connection.CreateCommand();

        using var tx = connection.BeginTransaction();
        
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT COUNT(*) FROM file_operations WHERE state NOT IN ('COMMITTED', 'FAILED');";
        if (Convert.ToInt32(cmd.ExecuteScalar()) != 0)
            throw new InvalidOperationException("An unresolved file operation must be completed before clearing the library.");

        cmd.CommandText = "SELECT COUNT(*) FROM movies;";
        var movies = Convert.ToInt32(cmd.ExecuteScalar());
        cmd.CommandText = "SELECT COUNT(*) FROM media_files;";
        var mediaFiles = Convert.ToInt32(cmd.ExecuteScalar());
        cmd.CommandText = "SELECT COUNT(*) FROM metadata_cache;";
        var metadata = Convert.ToInt32(cmd.ExecuteScalar());

        foreach (var table in new[] { "metadata_cache", "watch_events", "movie_personal_state", "media_files", "movies" })
        {
            cmd.CommandText = $"DELETE FROM {table};";
            cmd.ExecuteNonQuery();
        }

        cmd.CommandText = "PRAGMA foreign_key_check;";
        using (var violations = cmd.ExecuteReader())
        {
            if (violations.Read()) throw new InvalidOperationException("Catalog clear violated a foreign key.");
        }
        
        tx.Commit();
        
        return (movies, mediaFiles, metadata);
    }
}
