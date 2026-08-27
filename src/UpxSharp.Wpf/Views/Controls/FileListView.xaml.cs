using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using UpxSharp.Wpf.ViewModels;

namespace UpxSharp.Wpf.Views.Controls
{
    /// <summary>
    /// Code-behind do FileListView.
    /// A lógica de negócio permanece nos ViewModels; este arquivo
    /// cuida apenas de interações de UI que não podem ser feitas em XAML puro:
    ///   • Duplo clique → abrir pasta do arquivo
    ///   • Tecla Delete → remover arquivos selecionados
    ///   • Menu de contexto com ações contextuais
    ///   • Menu de contexto com ações contextuais
    /// </summary>
    public partial class FileListView : UserControl
    {
        public FileListView()
        {
            InitializeComponent();
        }

        // ── Dependência: ICommand para abrir pasta ──────────────────────────────

        /// <summary>
        /// Comando invocado ao dar duplo clique em uma linha.
        /// Esperado binding: Command="{Binding DataContext.OpenFolderCommand,
        ///     RelativeSource={RelativeSource AncestorType=Window}}"
        /// </summary>
        public static readonly DependencyProperty OpenFolderCommandProperty =
            DependencyProperty.Register(
                nameof(OpenFolderCommand),
                typeof(ICommand),
                typeof(FileListView));

        public ICommand? OpenFolderCommand
        {
            get => (ICommand?)GetValue(OpenFolderCommandProperty);
            set => SetValue(OpenFolderCommandProperty, value);
        }

        // ── Tecla Delete — remove arquivos selecionados ─────────────────────────

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key != Key.Delete) return;
            e.Handled = true;
            RemoveSelectedFiles();
        }

        private void RemoveSelectedFiles()
        {
            // Obtém todos os FileItemViewModel selecionados e remove via comando no MainViewModel
            var selected = FileGrid.SelectedItems
                .OfType<FileItemViewModel>()
                .ToList();

            if (selected.Count == 0) return;

            // Sobe até o MainViewModel via árvore de DataContext
            if (GetMainViewModel() is not { } vm) return;

            foreach (var item in selected)
            {
                if (vm.RemoveFileCommand.CanExecute(item))
                    vm.RemoveFileCommand.Execute(item);
            }
        }

        // ── Duplo clique — abre explorador na pasta do arquivo ─────────────────

        private void FileGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileGrid.SelectedItem is not FileItemViewModel item) return;

            var folder = Path.GetDirectoryName(item.FilePath);
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;

            // Abre o Explorer com o arquivo selecionado
            try
            {
                System.Diagnostics.Process.Start("explorer.exe",
                    $"/select,\"{item.FilePath}\"");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erro ao abrir Explorer: {ex.Message}");
            }
        }

        // ── Menu de contexto — construído dinamicamente ─────────────────────────

        private void FileGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (FileGrid.ContextMenu is null) return;

            bool hasSelection = FileGrid.SelectedItems.Count > 0;
            bool singleItem   = FileGrid.SelectedItems.Count == 1;

            // Atualiza o estado dos itens do menu de contexto
            foreach (MenuItem mi in FileGrid.ContextMenu.Items.OfType<MenuItem>())
            {
                mi.IsEnabled = mi.Name switch
                {
                    "MenuItemOpenFolder"    => singleItem,
                    "MenuItemCopyPath"      => singleItem,
                    "MenuItemRemove"        => hasSelection,
                    "MenuItemRemoveAll"     => FileGrid.Items.Count > 0,
                    _                       => true
                };
            }
        }

        private void MenuItemOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is not FileItemViewModel item) return;
            var folder = Path.GetDirectoryName(item.FilePath);
            if (!string.IsNullOrEmpty(folder) && Directory.Exists(folder))
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{item.FilePath}\"");
        }

        private void MenuItemCopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (FileGrid.SelectedItem is not FileItemViewModel item) return;
            Clipboard.SetText(item.FilePath);
        }

        private void MenuItemRemove_Click(object sender, RoutedEventArgs e)
            => RemoveSelectedFiles();

        private void MenuItemRemoveAll_Click(object sender, RoutedEventArgs e)
        {
            var command = GetMainViewModel()?.ClearFilesCommand;
            if (command?.CanExecute(null) == true)
                command.Execute(null);
        }

        // ── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Navega pela árvore visual para encontrar o MainViewModel.
        /// Primeiro tenta o DataContext da janela pai; depois o do próprio controle.
        /// </summary>
        private MainViewModel? GetMainViewModel()
        {
            // DataContext da janela raiz
            if (Window.GetWindow(this)?.DataContext is MainViewModel vm) return vm;
            // Fallback: DataContext do próprio UserControl
            if (DataContext is MainViewModel vm2) return vm2;
            return null;
        }
    }
}
