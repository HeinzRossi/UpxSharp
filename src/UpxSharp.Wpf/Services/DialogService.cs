namespace UpxSharp.Wpf.Services
{
    public sealed class DialogService : IDialogService
    {
        public string[]? OpenFiles(string title, string filter)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title           = title,
                Filter          = filter,
                Multiselect     = true,
                CheckFileExists = true,
            };
            return dlg.ShowDialog() == true ? dlg.FileNames : null;
        }

        public string? OpenFolder(string title)
        {
            // No .NET 10 / WPF: usar OpenFolderDialog
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title };
            return dlg.ShowDialog() == true ? dlg.FolderName : null;
        }
    }
}