namespace DropSort.FileSystem.Safety;

public class FileSafetyException : IOException
{
    public FileSafetyException(string message) : base(message) { }
}

public sealed class UnsafePathException : FileSafetyException
{
    public UnsafePathException(string message) : base(message) { }
}

public sealed class LinkTraversalException : FileSafetyException
{
    public LinkTraversalException(string message) : base(message) { }
}

public sealed class SourceMissingException : FileSafetyException
{
    public SourceMissingException(string message) : base(message) { }
}

public sealed class SourceChangedException : FileSafetyException
{
    public SourceChangedException(string message) : base(message) { }
}

public sealed class SameFileException : FileSafetyException
{
    public SameFileException(string message) : base(message) { }
}

public sealed class DestinationExistsException : FileSafetyException
{
    public DestinationExistsException(string message) : base(message) { }
}

public sealed class CaseInsensitiveCollisionException : FileSafetyException
{
    public CaseInsensitiveCollisionException(string message) : base(message) { }
}

public sealed class InvalidRenameException : FileSafetyException
{
    public InvalidRenameException(string message) : base(message) { }
}
