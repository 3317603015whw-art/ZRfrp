using System.Security.Cryptography;

namespace ZRfrp.Server;

public sealed class PortAllocationCoordinator
{
    public SemaphoreSlim Gate { get; } = new(1, 1);

    public static int SelectRandomPort(int start, int end, IReadOnlySet<int> reserved)
    {
        if (start < 1 || end > 65535 || start > end)
        {
            return 0;
        }

        var available = Enumerable.Range(start, end - start + 1)
            .Where(port => !reserved.Contains(port))
            .ToArray();
        return available.Length == 0
            ? 0
            : available[RandomNumberGenerator.GetInt32(available.Length)];
    }

    public static int SelectRandomPort(IReadOnlyList<int> available) =>
        available.Count == 0 ? 0 : available[RandomNumberGenerator.GetInt32(available.Count)];
}
