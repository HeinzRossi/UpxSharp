namespace UpxSharp.Wpf.Services
{
    public interface IDialogService
    {
        string[]? OpenFiles(string title, string filter);
        string?   OpenFolder(string title);
    }
}