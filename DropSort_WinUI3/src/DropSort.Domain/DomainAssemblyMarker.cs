namespace DropSort.Domain;

/// <summary>
/// Assembly marker for DropSort.Domain.
/// Domain entities, enums, and value objects will be added in Phase 2.
/// </summary>
public static class DomainAssemblyMarker
{
    public static string AssemblyName => typeof(DomainAssemblyMarker).Assembly.GetName().Name!;
}
