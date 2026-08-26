using System.Windows;
using System.Windows.Controls;
using UpxSharp.Wpf.ViewModels;

namespace UpxSharp.Wpf.Views.Controls
{
    public partial class FileDropPanel : UserControl
    {
        public FileDropPanel() => InitializeComponent();

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
                DataContext is MainViewModel vm)
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                vm.FilesDroppedCommand.Execute(files);
                vm.IsDragOver = false;
            }

            e.Handled = true;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsDragOver = e.Data.GetDataPresent(DataFormats.FileDrop);
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            if (DataContext is MainViewModel vm)
                vm.IsDragOver = false;
        }
    }
}
