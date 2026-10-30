namespace DropSort.FileSystem;

/// <summary>
/// Assembly marker for DropSort.FileSystem.
/// File Engine, PathPolicy, and SafeTransfer will be added in Phase 2.
/// </summary>
public static class FileSystemAssemblyMarker
{
    public static string AssemblyName => typeof(FileSystemAssemblyMarker).Assembly.GetName().Name!;
}
