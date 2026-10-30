namespace DropSort.Application;

/// <summary>
/// Assembly marker for DropSort.Application.
/// Use case interfaces, DTOs, and persistence abstractions will be added in Phase 2.
/// </summary>
public static class ApplicationAssemblyMarker
{
    public static string AssemblyName => typeof(ApplicationAssemblyMarker).Assembly.GetName().Name!;
}
