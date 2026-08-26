using System.IO;
using System.Runtime.InteropServices;
using UpxSharp.Shell.Interop;

namespace UpxSharp.Shell.ExplorerCommand;

internal static class ShellSelectionHelper
{
    private const string ExecutableExtension = ".exe";

    public static IReadOnlyList<string> GetPaths(IShellItemArray? items)
    {
        if (items is null)
            return [];

        try
        {
            items.GetCount(out uint count);
            var paths = new List<string>(checked((int)count));

            for (uint i = 0; i < count; i++)
            {
                items.GetItemAt(i, out IShellItem item);
                item.GetDisplayName(Sigdn.SIGDN_FILESYSPATH, out string path);

                if (!string.IsNullOrWhiteSpace(path))
                    paths.Add(path);
            }

            return paths;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or ArgumentException)
        {
            return [];
        }
    }

    public static bool HasSupportedSelection(IShellItemArray? items, bool okToBeSlow)
        => HasSupportedPaths(GetPaths(items), okToBeSlow);

    public static bool HasSupportedPaths(IEnumerable<string> paths, bool okToBeSlow)
    {
        bool hasAny = false;

        foreach (string path in paths)
        {
            hasAny = true;
            if (!HasSupportedPath(path, okToBeSlow))
                return false;
        }

        return hasAny;
    }

    private static bool HasSupportedPath(string path, bool okToBeSlow)
    {
        if (!string.Equals(Path.GetExtension(path), ExecutableExtension, StringComparison.OrdinalIgnoreCase))
            return false;

        return !okToBeSlow || HasMzHeader(path);
    }

    private static bool HasMzHeader(string path)
    {
        try
        {
            Span<byte> header = stackalloc byte[2];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return stream.Read(header) == 2 && header[0] == 0x4D && header[1] == 0x5A;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
