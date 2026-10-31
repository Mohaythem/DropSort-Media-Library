using Xunit;

// TBDMovies's tests exercise real SQLite databases and the process-global SQLite connection pool
// (SqliteConnection.ClearAllPools, WAL sidecars, file replacement during restore). Running test
// collections in parallel let one database test disturb another's pooled connections and produced
// intermittent, non-deterministic failures. Serializing the suite removes that contention so
// verification is reliable. The full suite still completes in seconds.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
