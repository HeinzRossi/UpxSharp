using System.IO;
using System.Windows;

namespace UpxSharp.Wpf.Services;

public static class DragDropFileHelper
{
    public static string[] GetExistingFiles(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
            return [];

        if (data.GetData(DataFormats.FileDrop) is not string[] paths)
            return [];

        return GetExistingFiles(paths);
    }

    public static string[] GetExistingFiles(IEnumerable<string> paths)
        => paths
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .ToArray();

    public static DragDropEffects GetDropEffects(IDataObject data)
        => GetExistingFiles(data).Length > 0
            ? DragDropEffects.Copy
            : DragDropEffects.None;
}
