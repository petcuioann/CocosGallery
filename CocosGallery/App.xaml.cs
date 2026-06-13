using Microsoft.UI.Xaml;
using System.Linq;
using Microsoft.Windows.AppLifecycle;
using System;

namespace CocosGallery
{
    public partial class App : Application
    {
        public static App? Instance { get; private set; }


        public App()
        {
            Instance = this;
            this.InitializeComponent();
        }

        public static System.Collections.Generic.HashSet<MainWindow> ActiveWindows { get; } = new();
        private H.NotifyIcon.TaskbarIcon? _trayIcon;

        private Microsoft.UI.Dispatching.DispatcherQueue? _mainDispatcher;

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _mainDispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            InitializeTrayIcon();
            UpdateKeepAlive();
            SpawnNewWindow();
        }

        public void SpawnNewWindow()
        {
            var hiddenWindow = ActiveWindows.FirstOrDefault(w => w.AppWindow.IsVisible == false);
            if (hiddenWindow != null)
            {
                hiddenWindow.ResetToStartingPoint();
                hiddenWindow.ShowAndBringToFront();
                return;
            }

            var newWindow = new MainWindow();
            newWindow.Closed += (s, e) => ActiveWindows.Remove(newWindow);
            ActiveWindows.Add(newWindow);
            newWindow.Activate();
        }

        public void UpdateKeepAlive()
        {
        // pre: app starts up
        // post: empty shell for future keep-alive logic
        // except: none
        // complexity: O(1)
        }

        private void InitializeTrayIcon()
        {
            var menu = new Microsoft.UI.Xaml.Controls.MenuFlyout();

            var showItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Show Cocos Gallery" };
            showItem.Icon = new Microsoft.UI.Xaml.Controls.FontIcon { Glyph = "\xE7BC" };
            showItem.Click += ShowApp_Click;

            var exitItem = new Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text = "Exit App" };
            exitItem.Icon = new Microsoft.UI.Xaml.Controls.SymbolIcon { Symbol = Microsoft.UI.Xaml.Controls.Symbol.Cancel };
            exitItem.Click += ExitApp_Click;

            menu.Items.Add(showItem);
            menu.Items.Add(exitItem);

            _trayIcon = new H.NotifyIcon.TaskbarIcon
            {
                ToolTipText = "Cocos Gallery",
                IconSource = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new System.Uri("ms-appx:///Assets/icon.ico")),
                ContextFlyout = menu
            };

            _trayIcon.DoubleTapped += TrayIcon_DoubleTapped;
            _trayIcon.ForceCreate();
        }

        public void OnActivatedByInstance(AppActivationArguments args)
        {
            _mainDispatcher?.TryEnqueue(() =>
            {
                SpawnNewWindow();
            });
        }

        private void TrayIcon_DoubleTapped(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            SpawnNewWindow();
        }

        private void ShowApp_Click(object sender, RoutedEventArgs e)
        {
            SpawnNewWindow();
        }

        // pre: user clicked exit on the tray icon
        // post: cleans up tray icon, optionally wipes cache (VideoThumbnails, RecycleBin, TemporaryFolder, LocalCacheFolder), and terminates application
        // except: ignores I/O exceptions during deletion
        // complexity: O(n) where n is the number of cached files
        private async void ExitApp_Click(object sender, RoutedEventArgs e)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            bool wipeCache = false;
            if (Windows.Storage.ApplicationData.Current.LocalSettings.Values.TryGetValue("WipeCacheOnClose", out object wcc) && wcc is bool wccVal)
            {
                wipeCache = wccVal;
            }

            if (wipeCache)
            {
                foreach (var window in ActiveWindows.ToList())
                {
                    window.ClearViewers();
                }

                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                await System.Threading.Tasks.Task.Delay(100);

                try
                {
                    string cacheDir = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "VideoThumbnails");
                    if (System.IO.Directory.Exists(cacheDir)) System.IO.Directory.Delete(cacheDir, true);
                    string recycleDir = System.IO.Path.Combine(Windows.Storage.ApplicationData.Current.LocalFolder.Path, "RecycleBin");
                    if (System.IO.Directory.Exists(recycleDir)) System.IO.Directory.Delete(recycleDir, true);

                    Windows.Storage.StorageFolder tempFolder = Windows.Storage.ApplicationData.Current.TemporaryFolder;
                    var tempFiles = await tempFolder.GetFilesAsync();
                    foreach (var file in tempFiles)
                    {
                        try { await file.DeleteAsync(Windows.Storage.StorageDeleteOption.PermanentDelete); } catch { }
                    }

                    Windows.Storage.StorageFolder localCacheFolder = Windows.Storage.ApplicationData.Current.LocalCacheFolder;
                    var cacheFiles = await localCacheFolder.GetFilesAsync();
                    foreach (var file in cacheFiles)
                    {
                        try { await file.DeleteAsync(Windows.Storage.StorageDeleteOption.PermanentDelete); } catch { }
                    }
                }
                catch { }
            }

            foreach (var window in ActiveWindows.ToList()) window.Close();
            Exit();
        }
    }
}