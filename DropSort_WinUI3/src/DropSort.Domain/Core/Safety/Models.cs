namespace DropSort.Domain.Core.Safety;

public sealed record SourceIdentity
{
    public long Size { get; }
    public long MTimeNs { get; }
    public ulong Dev { get; }
    public ulong Ino { get; }

    public SourceIdentity(long size, long mTimeNs, ulong dev, ulong ino)
    {
        if (size < 0) throw new ArgumentOutOfRangeException(nameof(size));
        Size = size;
        MTimeNs = mTimeNs;
        Dev = dev;
        Ino = ino;
    }
}
