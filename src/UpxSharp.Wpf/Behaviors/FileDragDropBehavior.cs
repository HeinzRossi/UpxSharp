using System.IO;
using System.Windows;
using System.Windows.Input;
using Microsoft.Xaml.Behaviors;

namespace UpxSharp.Wpf.Behaviors
{
    /// <summary>
    /// Behavior anexável que habilita drag-and-drop de arquivos em qualquer UIElement.
    /// Uso em XAML:
    ///   xmlns:b="clr-namespace:UpxSharp.Wpf.Behaviors"
    ///   xmlns:i="http://schemas.microsoft.com/xaml/behaviors"
    ///   &lt;i:Interaction.Behaviors&gt;
    ///       &lt;b:FileDragDropBehavior Command="{Binding FilesDroppedCommand}" /&gt;
    ///   &lt;/i:Interaction.Behaviors&gt;
    /// </summary>
    public sealed class FileDragDropBehavior : Behavior<UIElement>
    {
        // ── Dependency Properties ──────────────────────────────────────────────────

        /// <summary>
        /// Comando executado quando arquivos são soltos.
        /// O parâmetro passado é string[] com os caminhos dos arquivos.
        /// </summary>
        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.Register(
                nameof(Command),
                typeof(ICommand),
                typeof(FileDragDropBehavior),
                new PropertyMetadata(null));

        /// <summary>
        /// Propriedade bindável que indica se o usuário está arrastando sobre o controle.
        /// Útil para alterar visual durante o drag (ex: mudar cor da borda).
        /// </summary>
        public static readonly DependencyProperty IsDragOverProperty =
            DependencyProperty.Register(
                nameof(IsDragOver),
                typeof(bool),
                typeof(FileDragDropBehavior),
                new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        /// <summary>
        /// Extensões de arquivo aceitas (ex: ".exe;.dll;.so").
        /// Null ou vazio = aceita tudo.
        /// </summary>
        public static readonly DependencyProperty AllowedExtensionsProperty =
            DependencyProperty.Register(
                nameof(AllowedExtensions),
                typeof(string),
                typeof(FileDragDropBehavior),
                new PropertyMetadata(null));

        // ── Propriedades CLR ──────────────────────────────────────────────────────

        public ICommand? Command
        {
            get => (ICommand?)GetValue(CommandProperty);
            set => SetValue(CommandProperty, value);
        }

        public bool IsDragOver
        {
            get => (bool)GetValue(IsDragOverProperty);
            set => SetValue(IsDragOverProperty, value);
        }

        public string? AllowedExtensions
        {
            get => (string?)GetValue(AllowedExtensionsProperty);
            set => SetValue(AllowedExtensionsProperty, value);
        }

        // ── Ciclo de vida do Behavior ─────────────────────────────────────────────

        protected override void OnAttached()
        {
            base.OnAttached();
            AssociatedObject.AllowDrop = true;
            AssociatedObject.Drop      += OnDrop;
            AssociatedObject.DragOver  += OnDragOver;
            AssociatedObject.DragEnter += OnDragEnter;
            AssociatedObject.DragLeave += OnDragLeave;
        }

        protected override void OnDetaching()
        {
            base.OnDetaching();
            AssociatedObject.Drop      -= OnDrop;
            AssociatedObject.DragOver  -= OnDragOver;
            AssociatedObject.DragEnter -= OnDragEnter;
            AssociatedObject.DragLeave -= OnDragLeave;
        }

        // ── Handlers de evento ────────────────────────────────────────────────────

        private void OnDragEnter(object sender, DragEventArgs e)
        {
            if (ContainsFiles(e))
                IsDragOver = true;
        }

        private void OnDragLeave(object sender, DragEventArgs e)
        {
            IsDragOver = false;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (ContainsFiles(e))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            IsDragOver = false;

            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            var allFiles = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (allFiles is null || allFiles.Length == 0) return;

            // Filtra por extensão, se configurado
            var filtered = FilterByExtension(allFiles);
            if (filtered.Length == 0) return;

            if (Command?.CanExecute(filtered) == true)
                Command.Execute(filtered);

            e.Handled = true;
        }

        // ── Helpers ───────────────────────────────────────────────────────────────

        private static bool ContainsFiles(DragEventArgs e)
            => e.Data.GetDataPresent(DataFormats.FileDrop);

        /// <summary>
        /// Filtra arquivos pelas extensões configuradas em <see cref="AllowedExtensions"/>.
        /// Retorna todos os arquivos se nenhuma extensão for configurada.
        /// </summary>
        private string[] FilterByExtension(string[] files)
        {
            var ext = AllowedExtensions;
            if (string.IsNullOrWhiteSpace(ext)) return files;

            // Parse "*.exe;*.dll;.so" → {".exe", ".dll", ".so"}
            var allowed = ext
                .Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.TrimStart('*').ToLowerInvariant())
                .ToHashSet();

            return files
                .Where(f => allowed.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .ToArray();
        }
    }
}