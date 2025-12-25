using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TextGridToOto_CSharpVer.ViewModels;

namespace TextGridToOto_CSharpVer.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            
            // 监听 DataContext 变化
            DataContextChanged += OnDataContextChanged;
        }

        private void OnDataContextChanged(object? sender, System.EventArgs e)
        {
            // 如果之前有绑定，先取消订阅
            if (sender is Window window && window.DataContext is MainWindowViewModel oldViewModel)
            {
                oldViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }

            // 绑定新的 ViewModel
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // 当 LogText 属性变化时，自动滚动到底部
            if (e.PropertyName == nameof(MainWindowViewModel.LogText))
            {
                Dispatcher.UIThread.Post(() =>
                {
                    LogScrollViewer?.ScrollToEnd();
                }, DispatcherPriority.Background);
            }
        }

        private async void OnSelectWavFolderClick(object sender, RoutedEventArgs e)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "选择 Wav 文件夹",
                AllowMultiple = false
            });

            if (folders.Count > 0 && DataContext is MainWindowViewModel viewModel)
            {
                viewModel.WavFolderSelectedCommand.Execute(folders[0].Path.LocalPath);
            }
        }

        private async void OnSelectPresampFolderClick(object sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择 Presamp 文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Presamp Files") { Patterns = new[] { "*.ini" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (files.Count > 0 && DataContext is MainWindowViewModel viewModel)
            {
                viewModel.PresampFileSelectedCommand.Execute(files[0].Path.LocalPath);
            }
        }

        private async void OnSelectConfigFolderClick(object sender, RoutedEventArgs e)
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择 Config 文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("YAML Files") { Patterns = new[] { "*.yaml" } },
                    new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
                }
            });

            if (files.Count > 0 && DataContext is MainWindowViewModel viewModel)
            {
                viewModel.ConfigFileSelectedCommand.Execute(files[0].Path.LocalPath);
            }
        }
    }
}