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

            bool runInBackground = true;
            try {
                if (AppStorage.LocalSettings.Values.TryGetValue("RunInBackground", out object? rib) && rib is bool ribVal)
                    runInBackground = ribVal;
            } catch { }
            UpdateTrayVisibility(runInBackground);

            UpdateKeepAlive();
            SpawnNewWindow();
        }

        public void UpdateTrayVisibility(bool isVisible)
        {
            if (_trayIcon != null)
            {
                _trayIcon.Visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
            }
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

            _trayIcon = (H.NotifyIcon.TaskbarIcon)this.Resources["TrayIcon"];
            _trayIcon.ContextFlyout = menu;

            _trayIcon.DoubleTapped += TrayIcon_DoubleTapped;
            var leftClickCmd = new Microsoft.UI.Xaml.Input.XamlUICommand();
            leftClickCmd.ExecuteRequested += (s, e) => SpawnNewWindow();
            _trayIcon.LeftClickCommand = leftClickCmd;
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

            bool wipeCache = false;
            try {
                if (AppStorage.LocalSettings.Values.TryGetValue("WipeCacheOnClose", out object? wcc) && wcc is bool wccVal)
                {
                    wipeCache = wccVal;
                }
            } catch { }

            if (wipeCache)
            {
                foreach (var window in ActiveWindows.ToList())
                {
                    window.ClearViewers();
                }

                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                await System.Threading.Tasks.Task.Delay(100);

                    try {
                        string localPath = AppStorage.LocalFolderPath;
                        string cacheDir = System.IO.Path.Combine(localPath, "VideoThumbnails");
                        if (System.IO.Directory.Exists(cacheDir)) System.IO.Directory.Delete(cacheDir, true);
                        string recycleDir = System.IO.Path.Combine(localPath, "RecycleBin");
                        if (System.IO.Directory.Exists(recycleDir)) System.IO.Directory.Delete(recycleDir, true);

                        string tempFolder = AppStorage.TemporaryFolderPath;
                        if (System.IO.Directory.Exists(tempFolder)) {
                            foreach (var file in System.IO.Directory.GetFiles(tempFolder)) {
                                try { System.IO.File.Delete(file); } catch { }
                            }
                        }

                        string localCacheFolder = AppStorage.LocalCacheFolderPath;
                        if (System.IO.Directory.Exists(localCacheFolder)) {
                            foreach (var file in System.IO.Directory.GetFiles(localCacheFolder)) {
                                try { System.IO.File.Delete(file); } catch { }
                            }
                        }
                    } catch { }
            }

            foreach (var window in ActiveWindows.ToList()) window.Close();
            
            if (_trayIcon != null)
            {
                try { _trayIcon.Dispose(); } catch { }
                _trayIcon = null;
            }

            Application.Current.Exit();
            Environment.Exit(0);
        }
    }
}
