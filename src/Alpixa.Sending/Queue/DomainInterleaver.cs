namespace Alpixa.Sending.Queue;

public static class DomainInterleaver
{
    public static List<T> Interleave<T>(IEnumerable<T> items, Func<T, string> domainSelector)
    {
        var positioned = new List<(double Position, int Bucket, T Item)>();
        var bucketIndex = 0;
        foreach (var group in items.GroupBy(domainSelector, StringComparer.OrdinalIgnoreCase))
        {
            var members = group.ToList();
            for (var k = 0; k < members.Count; k++)
                positioned.Add(((k + 0.5) / members.Count, bucketIndex, members[k]));
            bucketIndex++;
        }

        positioned.Sort((a, b) =>
        {
            var c = a.Position.CompareTo(b.Position);
            return c != 0 ? c : a.Bucket.CompareTo(b.Bucket);
        });
        return positioned.Select(p => p.Item).ToList();
    }
}
