using CocosGallery;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Media.Core;
using Windows.Storage;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage.Pickers;

namespace CocosGallery {
    public sealed partial class MainWindow : Window {
        public MainViewModel ViewModel { get; } = new MainViewModel();

        private bool _isDragging = false;
        private Point _lastPointerPosition;
        private bool _isDragSelecting = false;
        private bool _isRightDrag = false;
        private Point _dragSelectStartPos;

        private MediaItem? _lastSelectedAnchor = null;
        private MediaItem? _pressedItem = null;
        private MediaItem? _rightClickedItem = null;

        private long _lastClickTicks = 0;
        private Point _lastClickPos;
        private bool _isDoubleClickDrag = false;
        private MediaItem? _doubleClickStartItem = null;
        private List<MediaItem> _previousSelection = new List<MediaItem>();

        private DispatcherTimer _singleClickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };

        private bool _isSidebarHovered = false;
        private DispatcherTimer _idleTimer = new DispatcherTimer();
        private bool _isHudVisible = true;
        private DateTime _lastHudHideTime = DateTime.MinValue;
        private bool _isDebouncing = false;

        private DispatcherTimer _resizeTimer = new DispatcherTimer();
        private int _resizeStartWidth, _resizeStartHeight, _resizeTargetWidth, _resizeTargetHeight;
        private Stopwatch _resizeStopwatch = new Stopwatch();
        private const double ResizeDurationMs = 500;
        private int _defaultLibraryWidth = 1000;

        private enum ActionPanelState { Vertical, Horizontal, Overflow }
        /// <summary>111111111111111 the mats up the custom title bar, sidebar layout, and global event handlers.</summary>
        private ActionPanelState _currentActionState = ActionPanelState.Horizontal;
        private List<UIElement> _actionButtons = new List<UIElement>();
        private Rectangle? _actionSeparator;

        /// <summary>Initializes the main window, sets up the custom title bar, sidebar layout, and global event handlers.</summary>
        public MainWindow() {
            this.InitializeComponent();

            string iconPath = System.IO.Path.Combine(System.AppContext.BaseDirectory, "Assets", "icon.ico");
            if (System.IO.File.Exists(iconPath)) this.AppWindow.SetIcon(iconPath);

            if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.SetBorderAndTitleBar(true, false);
            this.AppWindow.Resize(new SizeInt32(1280, 720));
            this.RootGrid.DataContext = this.ViewModel;
            this.AppWindow.Closing += AppWindow_Closing;

            this.ViewModel.PropertyChanged += (s, e) => {
                if (e.PropertyName == nameof(ViewModel.IsPreferContextMenus)) UpdateActionPanelLayout();
                if (e.PropertyName == nameof(ViewModel.IsAutoShrinkSidebar)) AutoShrinkToggle_Changed();
            };

            this.RootGrid.Loaded += (s, e) => {
                UpdateTitleBarRegions();
                _defaultLibraryWidth = this.AppWindow.Size.Width;

                if (!ViewModel.IsAutoShrinkSidebar) { ViewModel.IsSidebarVisible = true; AnimateSidebar(true); }
                else { SidebarTranslate.X = -260; SidebarGrid.Visibility = Visibility.Collapsed; }

                _actionButtons = CollapsibleButtons.Children.ToList();
                _actionSeparator = _actionButtons.OfType<Rectangle>().FirstOrDefault();
                UpdateActionPanelLayout();
            };

            this.RootGrid.SizeChanged += (s, e) => {
                UpdateTitleBarRegions();
                UpdateActionPanelLayout();
                if (this.ViewerOverlay.Visibility != Visibility.Visible) _defaultLibraryWidth = this.AppWindow.Size.Width;
            };

            ImageGridView.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(ImageGridView_PointerPressed), true);
            ImageGridView.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(ImageGridView_PointerMoved), true);
            ImageGridView.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(ImageGridView_PointerReleased), true);

            _idleTimer.Interval = TimeSpan.FromSeconds(5);
            _idleTimer.Tick += IdleTimer_Tick;

            _singleClickTimer.Tick += SingleClickTimer_Tick;

            _resizeTimer.Interval = TimeSpan.FromMilliseconds(16);
            _resizeTimer.Tick += ResizeTimer_Tick;
        }

        /// <summary>Resets the window's selection, viewer state, and navigation back to the root folder.</summary>
        public void ResetToStartingPoint() {
            this.ViewModel.ClearSelection();
            if (MainViewModel.IsViewerOpen) { MainViewModel.IsViewerOpen = false; this.ViewerOverlay.Visibility = Visibility.Collapsed; this.ViewerImage.Source = null!; this.ViewerVideo.Source = null!; this.ViewModel.SelectedItem = null; StartWindowResizeAnimation(_defaultLibraryWidth, this.AppWindow.Size.Height); }
            if (this.ViewModel.SelectedFolder != this.ViewModel.Folders.FirstOrDefault()) this.ViewModel.SelectedFolder = this.ViewModel.Folders.FirstOrDefault();
            this.MediaScrollViewer.ChangeView(null, 0, null, true); AnimateHud(true);
        }
        /// <summary>Prompts the user to restart the application to apply the cross-memory toggle setting.</summary>
        private async void CrossMemoryToggle_Click(object sender, RoutedEventArgs e) {
            if (sender is ToggleMenuFlyoutItem toggle) { var dialog = new ContentDialog { Title = "Restart Required", Content = "The application needs to restart for the cross-memory selection setting to take effect. Would you like to restart now?", PrimaryButtonText = "Restart Now", CloseButtonText = "Later", XamlRoot = this.Content.XamlRoot }; if (await dialog.ShowAsync() == ContentDialogResult.Primary) Microsoft.Windows.AppLifecycle.AppInstance.Restart(""); }
        }
        /// <summary>Clears the viewer media sources and all loaded media items.</summary>
        public void ClearViewers() { this.ViewerImage.Source = null; this.ViewerVideo.Source = null; this.ViewModel.MediaItems.Clear(); this.ViewModel.ClearSelection(); }
        /// <summary>Prevents window closure and instead minimizes it to the system tray if background mode is enabled.</summary>
        private void AppWindow_Closing(Microsoft.UI.Windowing.AppWindow sender, Microsoft.UI.Windowing.AppWindowClosingEventArgs args) { if (App.ActiveWindows.Count == 1 && App.ActiveWindows.Contains(this) && this.ViewModel.IsRunInBackgroundEnabled) { args.Cancel = true; this.AppWindow.Hide(); return; } }
        /// <summary>Restores the window from a minimized/hidden state and brings it to the foreground.</summary>
        public void ShowAndBringToFront() { this.AppWindow.Show(); if (this.AppWindow.Presenter is OverlappedPresenter presenter && presenter.State == OverlappedPresenterState.Minimized) presenter.Restore(); SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this)); }
        /// <summary>Brings the specified window to the foreground and activates it.</summary>
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
        /// <summary>Configures custom title bar drag regions and standard control button areas.</summary>
        private void UpdateTitleBarRegions() {
            if (this.Content?.XamlRoot == null) return;
            double scale = this.Content.XamlRoot.RasterizationScale; int titleBarHeight = (int)(48 * scale); var dragRect = new RectInt32(0, 0, this.AppWindow.Size.Width, titleBarHeight); var nonClientSource = InputNonClientPointerSource.GetForWindowId(this.AppWindow.Id);
            nonClientSource.SetRegionRects(NonClientRegionKind.Caption, new[] { dragRect }); nonClientSource.SetRegionRects(NonClientRegionKind.Close, Array.Empty<RectInt32>()); nonClientSource.SetRegionRects(NonClientRegionKind.Minimize, Array.Empty<RectInt32>()); nonClientSource.SetRegionRects(NonClientRegionKind.Maximize, Array.Empty<RectInt32>());
        }
        /// <summary>Closes the window or hides it if background processing is active.</summary>
        private void TrafficLightClose_Click(object? sender, RoutedEventArgs? e) { if (App.ActiveWindows.Count == 1 && App.ActiveWindows.Contains(this)) { if (this.ViewModel.IsRunInBackgroundEnabled) this.AppWindow.Hide(); else this.Close(); } else this.Close(); }
        /// <summary>Minimizes the window.</summary>
        private void TrafficLightMinimize_Click(object? sender, RoutedEventArgs? e) { if (this.AppWindow.Presenter is OverlappedPresenter presenter) presenter.Minimize(); }
        /// <summary>Toggles window maximization state.</summary>
        private void TrafficLightMaximize_Click(object? sender, RoutedEventArgs? e) { if (this.AppWindow.Presenter is OverlappedPresenter presenter) { if (presenter.State == OverlappedPresenterState.Maximized) presenter.Restore(); else presenter.Maximize(); } }

        #region Sidebar Autohide & Toggles Logic

        /// <summary>Displays the sidebar automatically when hovering the left edge of the window in auto-shrink mode.</summary>
        private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e) { if (ViewModel.IsAutoShrinkSidebar && !ViewModel.IsSidebarVisible && e.GetCurrentPoint(RootGrid).Position.X <= 20) { ViewModel.IsSidebarVisible = true; AnimateSidebar(true); } }
        /// <summary>Opens the global settings context menu at the settings anchor.</summary>
        private void SettingsLight_Click(object sender, RoutedEventArgs e) { MenuFlyout? flyout = this.RootGrid.Resources["SettingsMenu"] as MenuFlyout; flyout?.ShowAt(SettingsAnchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.TopEdgeAlignedLeft }); }
        /// <summary>Re-evaluates sidebar visibility and animations when the auto-shrink toggle is changed.</summary>
        private void AutoShrinkToggle_Changed() { if (ViewModel.IsAutoShrinkSidebar) { ViewModel.IsSidebarVisible = _isSidebarHovered; AnimateSidebar(_isSidebarHovered); } else { ViewModel.IsSidebarVisible = true; AnimateSidebar(true); } }
        /// <summary>Hides the sidebar when the cursor exits its bounds in auto-shrink mode.</summary>
        private void SidebarGrid_PointerExited(object sender, PointerRoutedEventArgs e) { _isSidebarHovered = false; if (ViewModel.IsAutoShrinkSidebar && ViewModel.IsSidebarVisible && e.GetCurrentPoint(RootGrid).Position.X >= SidebarGrid.ActualWidth) { ViewModel.IsSidebarVisible = false; AnimateSidebar(false); } }
        /// <summary>Collapses the auto-shrink sidebar when the main content grid gains pointer focus.</summary>
        private void ContentGrid_PointerEntered(object sender, PointerRoutedEventArgs e) { if (ViewModel.IsAutoShrinkSidebar && ViewModel.IsSidebarVisible) { ViewModel.IsSidebarVisible = false; AnimateSidebar(false); } }

        /// <summary>Animates the sidebar sliding in and out, optionally pushing content based on pinned state.</summary>
        private void AnimateSidebar(bool show) {
            bool isPinned = !ViewModel.IsAutoShrinkSidebar;
            double targetSidebarX = show ? 0 : -260; double targetPushWidth = (show && isPinned) ? 260 : 0; double targetBottomOffset = show ? 260 : 0;
            var sb = new Storyboard(); var ease = new ExponentialEase { Exponent = 6, EasingMode = EasingMode.EaseOut }; var duration = new Duration(TimeSpan.FromSeconds(0.25));

            var sidebarAnim = new DoubleAnimation { To = targetSidebarX, Duration = duration, EasingFunction = ease }; Storyboard.SetTarget(sidebarAnim, SidebarTranslate); Storyboard.SetTargetProperty(sidebarAnim, "X");
            var pushAnim = new DoubleAnimation { To = targetPushWidth, Duration = duration, EasingFunction = ease, EnableDependentAnimation = true }; Storyboard.SetTarget(pushAnim, ContentPush); Storyboard.SetTargetProperty(pushAnim, "Width");
            var bottomAnim = new DoubleAnimation { To = targetBottomOffset, Duration = duration, EasingFunction = ease }; Storyboard.SetTarget(bottomAnim, BottomControlsTranslate); Storyboard.SetTargetProperty(bottomAnim, "X");

            sb.Children.Add(sidebarAnim); sb.Children.Add(pushAnim); sb.Children.Add(bottomAnim);
            if (show) SidebarGrid.Visibility = Visibility.Visible;
            sb.Completed += (s, ev) => { if (!show) SidebarGrid.Visibility = Visibility.Collapsed; }; sb.Begin();
        }

#endregion
        #region Focus Mode Adaptive HUD & Auto-Hide

        private void OverflowMenu_Opened(object sender, object e) { CollapseToggleButton.Opacity = 0; CollapseToggleButton.IsHitTestVisible = false; }
        private void OverflowMenu_Closed(object sender, object e) { CollapseToggleButton.Opacity = 1; CollapseToggleButton.IsHitTestVisible = true; }
        private void ViewerOverlay_RightTapped(object sender, RightTappedRoutedEventArgs e) { MenuFlyout? flyout = this.RootGrid.Resources["OverflowMenu"] as MenuFlyout; flyout?.ShowAt(ViewerOverlay, e.GetPosition(ViewerOverlay)); }
        private void StartIdleTimer() { _idleTimer.Stop(); _idleTimer.Start(); }
        private void StopIdleTimer() => _idleTimer.Stop();
        private void IdleTimer_Tick(object? sender, object e) { if (this.ViewerOverlay.Visibility == Visibility.Visible) AnimateHud(false); else StopIdleTimer(); }
        private void SingleClickTimer_Tick(object? sender, object e) => _singleClickTimer.Stop();

        /// <summary>Dynamically adjusts the action panel state (Horizontal, Vertical, or Overflow) based on the available window width and height.</summary>
        private void UpdateActionPanelLayout() {
            if (this.ViewerOverlay.Visibility != Visibility.Visible) return;
            double h = this.RootGrid.ActualHeight; double w = this.RootGrid.ActualWidth; ActionPanelState targetState;
            if (ViewModel.IsPreferContextMenus) targetState = ActionPanelState.Overflow; else if (h > 650) targetState = ActionPanelState.Vertical; else if (w > 650) targetState = ActionPanelState.Horizontal; else targetState = ActionPanelState.Overflow;
            if (_currentActionState != targetState) ApplyActionPanelState(targetState);
        }

        /// <summary>Applies the appropriate structural layout for the viewer's bottom action panel.</summary>
        private void ApplyActionPanelState(ActionPanelState state) {
            _currentActionState = state; BottomActionPanel.Children.Clear(); CollapsibleButtons.Children.Clear();
            if (_actionSeparator != null) { if (state == ActionPanelState.Vertical) { _actionSeparator.Width = 16; _actionSeparator.Height = 1; _actionSeparator.Margin = new Thickness(0, 4, 0, 4); } else { _actionSeparator.Width = 1; _actionSeparator.Height = 24; _actionSeparator.Margin = new Thickness(4, 0, 4, 0); } }

            if (state == ActionPanelState.Overflow) { BottomActionPanel.Orientation = Orientation.Horizontal; BottomActionPanel.Children.Add(CollapseToggleButton); CollapseToggleButton.Flyout = null!; CollapsibleButtons.Visibility = Visibility.Collapsed; }
            else if (state == ActionPanelState.Vertical) { BottomActionPanel.Orientation = Orientation.Vertical; CollapsibleButtons.Orientation = Orientation.Vertical; CollapsibleButtons.Visibility = this.ViewModel.AreActionButtonsVisible ? Visibility.Visible : Visibility.Collapsed; CollapseToggleButton.Flyout = null!; var reversed = new List<UIElement>(_actionButtons); reversed.Reverse(); foreach (var el in reversed) CollapsibleButtons.Children.Add(el); BottomActionPanel.Children.Add(CollapsibleButtons); BottomActionPanel.Children.Add(CollapseToggleButton); }
            else { BottomActionPanel.Orientation = Orientation.Horizontal; CollapsibleButtons.Orientation = Orientation.Horizontal; CollapsibleButtons.Visibility = this.ViewModel.AreActionButtonsVisible ? Visibility.Visible : Visibility.Collapsed; CollapseToggleButton.Flyout = null!; foreach (var el in _actionButtons) CollapsibleButtons.Children.Add(el); BottomActionPanel.Children.Add(CollapseToggleButton); BottomActionPanel.Children.Add(CollapsibleButtons); }

            UpdateToggleIcon();
            if (!_isHudVisible) { if (state == ActionPanelState.Horizontal) { BottomPanelTranslate.X = 0; BottomPanelTranslate.Y = 100; } else { BottomPanelTranslate.Y = 0; BottomPanelTranslate.X = -100; } } else { BottomPanelTranslate.X = 0; BottomPanelTranslate.Y = 0; }
        }

        /// <summary>Updates the expand/collapse icon depending on the panel orientation and visibility.</summary>
        private void UpdateToggleIcon() { bool isVis = this.ViewModel.AreActionButtonsVisible; if (_currentActionState == ActionPanelState.Overflow) ToggleIcon.Glyph = "\uE712"; else if (_currentActionState == ActionPanelState.Vertical) ToggleIcon.Glyph = isVis ? "\uE70D" : "\uE70E"; else ToggleIcon.Glyph = isVis ? "\uE76B" : "\uE76C"; }

        /// <summary>Toggles action button visibility or opens the overflow menu if there isn't enough space.</summary>
        private void CollapseToggleButton_Click(object sender, RoutedEventArgs e) { if (_currentActionState == ActionPanelState.Overflow) { MenuFlyout? flyout = this.RootGrid.Resources["OverflowMenu"] as MenuFlyout; flyout?.ShowAt(MenuAnchor, new FlyoutShowOptions { Placement = FlyoutPlacementMode.TopEdgeAlignedLeft }); return; } this.ViewModel.ToggleActionButtons(); CollapsibleButtons.Visibility = this.ViewModel.AreActionButtonsVisible ? Visibility.Visible : Visibility.Collapsed; UpdateToggleIcon(); }

        /// <summary>Animates the viewer Heads-Up Display (HUD) sliding in or out.</summary>
        private void AnimateHud(bool show) {
            if (_isHudVisible == show || (show && _isDebouncing)) return;
            _isHudVisible = show; var duration = TimeSpan.FromSeconds(0.3); var ease = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 4 };

            var topAnim = new DoubleAnimation { To = show ? 0 : -50, Duration = duration, EasingFunction = ease }; Storyboard topSb = new Storyboard(); Storyboard.SetTarget(topAnim, TopPanelTranslate); Storyboard.SetTargetProperty(topAnim, "Y"); topSb.Children.Add(topAnim); topSb.Begin();
            var bottomAnim = new DoubleAnimation { Duration = duration, EasingFunction = ease }; Storyboard bottomSb = new Storyboard(); Storyboard.SetTarget(bottomAnim, BottomPanelTranslate);

            if (_currentActionState == ActionPanelState.Horizontal) { bottomAnim.To = show ? 0 : 100; Storyboard.SetTargetProperty(bottomAnim, "Y"); BottomPanelTranslate.X = 0; } else { bottomAnim.To = show ? 0 : -100; Storyboard.SetTargetProperty(bottomAnim, "X"); BottomPanelTranslate.Y = 0; }

            bottomSb.Children.Add(bottomAnim); bottomSb.Begin();
            if (show) { if (this.ViewerOverlay.Visibility == Visibility.Visible) StartIdleTimer(); else StopIdleTimer(); }
            else { StopIdleTimer(); _lastHudHideTime = DateTime.Now; _isDebouncing = true; _ = Task.Run(async () => { await Task.Delay(500); _isDebouncing = false; }); }
        }

        /// <summary>Handles global keyboard shortcuts like viewer navigation, closing, and HUD toggling.</summary>
        private void RootGrid_KeyDown(object sender, KeyRoutedEventArgs e) {
            if (this.ViewerOverlay.Visibility == Visibility.Visible) {
                if (e.Key == VirtualKey.Space) { AnimateHud(!_isHudVisible); e.Handled = true; return; }
                if (!_isHudVisible) AnimateHud(true); StartIdleTimer();
                if (e.Key == VirtualKey.Left) _ = NavigateMedia(-1); else if (e.Key == VirtualKey.Right) _ = NavigateMedia(1); else if (e.Key == VirtualKey.Escape) CloseViewer_Click(null, null);
            }
            else if (e.Key == VirtualKey.Escape && this.ViewModel.IsSelectMode) { this.ViewModel.ClearSelection(); e.Handled = true; }
        }

        /// <summary>Wakes up the HUD when the pointer is moved over the viewer overlay.</summary>
        private void ViewerOverlay_PointerMoved(object sender, PointerRoutedEventArgs e) { if (this.ViewerOverlay.Visibility == Visibility.Visible) { if (!_isHudVisible && (DateTime.Now - _lastHudHideTime).TotalMilliseconds < 500) return; if (!_isHudVisible) AnimateHud(true); StartIdleTimer(); } }

        /// <summary>Handles right clicks, drag selection, and closing when clicking empty viewer space.</summary>
        private void ViewerOverlay_PointerPressed(object sender, PointerRoutedEventArgs e) {
            long currentTicks = DateTime.Now.Ticks;
            if (currentTicks - _lastClickTicks < TimeSpan.TicksPerMillisecond * 500) { var ptOverlay = e.GetCurrentPoint(ImageGridView); if (Math.Abs(ptOverlay.Position.X - _lastClickPos.X) < 10 && Math.Abs(ptOverlay.Position.Y - _lastClickPos.Y) < 10) { CloseViewer_Click(null, null); _isDoubleClickDrag = true; this.ViewModel.IsSelectMode = true; _dragSelectStartPos = ptOverlay.Position; _doubleClickStartItem = this.ViewModel.SelectedItem; _previousSelection = this.ViewModel.SelectedItems.ToList(); ImageGridView.CapturePointer(e.Pointer); return; } }
            if (!_isHudVisible && (DateTime.Now - _lastHudHideTime).TotalMilliseconds < 500) return;
            var pt = e.GetCurrentPoint(ViewerOverlay);
            if (!pt.Properties.IsRightButtonPressed) {
                var eSrc = e.OriginalSource as DependencyObject; bool clickedOnContent = false; DependencyObject current = eSrc;
                while (current != null && current != ViewerOverlay) { if (current == ViewerImage || current == ViewerVideo || current == BottomActionPanel || current == TopTrafficLights) { clickedOnContent = true; break; } current = VisualTreeHelper.GetParent(current); }
                if (!clickedOnContent) { CloseViewer_Click(null, null); return; }
                AnimateHud(!_isHudVisible);
            }
        }

#endregion
        #region Folder Actions (Add, Rename, Copy, Delete)

        private void FolderListView_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) => this.ViewModel.SaveFolders();
        private async void AddFolder_Click(object sender, RoutedEventArgs e) { var folderPicker = new FolderPicker(); folderPicker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, WinRT.Interop.WindowNative.GetWindowHandle(this)); StorageFolder? folder = await folderPicker.PickSingleFolderAsync(); if (folder != null) { var newFolder = new FolderItem { Name = folder.Name, Path = folder.Path }; this.ViewModel.Folders.Add(newFolder); this.ViewModel.SaveFolders(); this.ViewModel.SelectedFolder = newFolder; } }
        private async void OpenFolderDirectory_Click(object sender, RoutedEventArgs e) { if ((sender as MenuFlyoutItem)?.DataContext is FolderItem folderItem) { try { StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(folderItem.Path); await Launcher.LaunchFolderAsync(folder); } catch { } } }
        private async void RenameFolder_Click(object sender, RoutedEventArgs e) { if ((sender as MenuFlyoutItem)?.DataContext is FolderItem folderItem) { var tb = new TextBox { Text = folderItem.Name }; if (await new ContentDialog { Title = "Rename Folder", Content = tb, PrimaryButtonText = "Next", CloseButtonText = "Cancel", XamlRoot = this.RootGrid.XamlRoot }.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(tb.Text)) { string newName = tb.Text.Trim(); var result = await new ContentDialog { Title = "Rename Scope", Content = "Only rename it within this instance or proceed with renaming it in the system?", PrimaryButtonText = "System", SecondaryButtonText = "Instance Only", CloseButtonText = "Cancel", XamlRoot = this.RootGrid.XamlRoot }.ShowAsync(); if (result == ContentDialogResult.Primary) { try { StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(folderItem.Path); await folder.RenameAsync(newName, NameCollisionOption.GenerateUniqueName); folderItem.Name = folder.Name; folderItem.Path = folder.Path; this.ViewModel.SaveFolders(); } catch { } } else if (result == ContentDialogResult.Secondary) { folderItem.Name = newName; this.ViewModel.SaveFolders(); } } } }
        private async void CopyFolder_Click(object sender, RoutedEventArgs e) { if ((sender as MenuFlyoutItem)?.DataContext is FolderItem folderItem) { try { StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(folderItem.Path); var dataPackage = new DataPackage { RequestedOperation = DataPackageOperation.Copy }; dataPackage.SetStorageItems(new List<IStorageItem> { folder }); Clipboard.SetContent(dataPackage); ShowCopiedFlyout(_lastPointerPosition); } catch { } } }
        private async void DeleteFolder_Click(object sender, RoutedEventArgs e) { if ((sender as MenuFlyoutItem)?.DataContext is FolderItem folderItem) { if (await new ContentDialog { Title = "Delete Folder", Content = $"Are you sure you want to move '{folderItem.Name}' to the Recycle Bin?", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", XamlRoot = this.RootGrid.XamlRoot }.ShowAsync() == ContentDialogResult.Primary) { try { StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(folderItem.Path); await folder.DeleteAsync(StorageDeleteOption.Default); this.ViewModel.RemoveFolder(folderItem); } catch { } } } }

#endregion
        #region Universal Right-Click, Selection, and Context Menus

        private void ShowCopiedFlyout(Point location) { var f = new Flyout(); f.Content = new TextBlock { Text = "Copied to clipboard!", Margin = new Thickness(10) }; f.ShowAt(this.RootGrid, new FlyoutShowOptions { Position = location, Placement = FlyoutPlacementMode.Top }); }
        private async void SingleItemLocation_Click(object sender, RoutedEventArgs e) { if (_rightClickedItem == null) return; try { StorageFile f = await StorageFile.GetFileFromPathAsync(_rightClickedItem.FilePath); await Launcher.LaunchFolderAsync(await f.GetParentAsync(), new FolderLauncherOptions { ItemsToSelect = { f } }); } catch { } }
        private async void SingleItemRename_Click(object sender, RoutedEventArgs e) { if (_rightClickedItem == null) return; var tb = new TextBox { Text = _rightClickedItem.Title }; if (await new ContentDialog { Title = "Rename", Content = tb, PrimaryButtonText = "Rename", CloseButtonText = "Cancel", XamlRoot = this.RootGrid.XamlRoot }.ShowAsync() == ContentDialogResult.Primary) { try { StorageFile file = await StorageFile.GetFileFromPathAsync(_rightClickedItem.FilePath); string ext = file.FileType; string newName = tb.Text.EndsWith(ext, StringComparison.OrdinalIgnoreCase) ? tb.Text : tb.Text + ext; await file.RenameAsync(newName); _rightClickedItem.Title = newName; _rightClickedItem.FilePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_rightClickedItem.FilePath) ?? string.Empty, newName); } catch { } } }
        private void SingleItemCopy_Click(object sender, RoutedEventArgs e) { if (_rightClickedItem == null) return; var dataPackage = new DataPackage { RequestedOperation = DataPackageOperation.Copy }; _ = Task.Run(async () => { try { StorageFile file = await StorageFile.GetFileFromPathAsync(_rightClickedItem.FilePath); dataPackage.SetStorageItems(new List<IStorageItem> { file }); Clipboard.SetContent(dataPackage); } catch { } }); ShowCopiedFlyout(_lastPointerPosition); }
        private async void SingleItemDelete_Click(object sender, RoutedEventArgs e) { if (_rightClickedItem == null) return; MediaItem? temp = this.ViewModel.SelectedItem; this.ViewModel.SelectedItem = _rightClickedItem; await this.ViewModel.DeleteCurrentItem(); this.ViewModel.SelectedItem = temp; }

        /// <summary>Triggers the bulk rename dialog and updates the view model if a valid base name is provided.</summary>
        /// <remarks>Complexity: O(1)</remarks>
        private async void BulkRename_Click(object sender, RoutedEventArgs e) { var nameBox = new TextBox { PlaceholderText = "Enter base filename...", Width = 300 }; if (await new ContentDialog { Title = "Bulk Rename", Content = nameBox, PrimaryButtonText = "Rename", CloseButtonText = "Cancel", XamlRoot = this.RootGrid.XamlRoot, DefaultButton = ContentDialogButton.Primary }.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text)) await this.ViewModel.BulkRenameItems(nameBox.Text.Trim()); }
        private void EditTags_Click(object sender, RoutedEventArgs e) { var itemsToEdit = this.ViewModel.IsSelectMode ? this.ViewModel.SelectedItems.ToList() : new List<MediaItem>(); if (!this.ViewModel.IsSelectMode && _rightClickedItem != null) itemsToEdit.Add(_rightClickedItem); if (itemsToEdit.Count == 0) return; foreach (var tag in this.ViewModel.Tags) { if (!tag.IsEditable && tag.Name != "Favorites") continue; tag.IsCheckedForEdit = tag.Name == "Favorites" ? itemsToEdit.All(i => i.IsFavorite) : itemsToEdit.All(i => i.Tags.Contains(tag.Name)); } if (this.RootGrid.Resources["EditTagsFlyout"] as Flyout is Flyout flyout) { if (flyout.Content is FrameworkElement fe) fe.DataContext = this.ViewModel.Tags; var pt = _lastPointerPosition; this.DispatcherQueue.TryEnqueue(() => flyout.ShowAt(ImageGridView, new FlyoutShowOptions { Position = pt, Placement = FlyoutPlacementMode.Right })); } }
        private void EditTagsCancel_Click(object sender, RoutedEventArgs e) { if (this.RootGrid.Resources["EditTagsFlyout"] as Flyout is Flyout flyout) flyout.Hide(); }
        private void EditTagsDone_Click(object sender, RoutedEventArgs e) { var itemsToEdit = this.ViewModel.IsSelectMode ? this.ViewModel.SelectedItems.ToList() : new List<MediaItem>(); if (!this.ViewModel.IsSelectMode && _rightClickedItem != null) itemsToEdit.Add(_rightClickedItem); if (itemsToEdit.Count > 0) { var tagsToAdd = this.ViewModel.Tags.Where(t => (t.IsEditable || t.Name == "Favorites") && t.IsCheckedForEdit == true).Select(t => t.Name).ToList(); var tagsToRemove = this.ViewModel.Tags.Where(t => (t.IsEditable || t.Name == "Favorites") && t.IsCheckedForEdit == false).Select(t => t.Name).ToList(); this.ViewModel.BulkEditTags(itemsToEdit, tagsToAdd, tagsToRemove); } if (this.RootGrid.Resources["EditTagsFlyout"] as Flyout is Flyout flyout) flyout.Hide(); if (this.ViewModel.IsSelectMode) this.ViewModel.ClearSelection(); }
        private void EditFlyoutNewTag_Click(object sender, RoutedEventArgs e) { if (sender is Button btn && btn.Parent is Grid grid && grid.Children[0] is TextBox textBox) { string newTag = textBox.Text.Trim(); if (!string.IsNullOrEmpty(newTag)) { this.ViewModel.AddNewTag(newTag); var tagItem = this.ViewModel.Tags.FirstOrDefault(t => t.Name == newTag); if (tagItem != null) tagItem.IsCheckedForEdit = true; textBox.Text = ""; } } }
        private async void SingleItemRestore_Click(object sender, RoutedEventArgs e) { if (_rightClickedItem != null) await this.ViewModel.RestoreItem(_rightClickedItem); }
        private async void BulkRestore_Click(object sender, RoutedEventArgs e) => await this.ViewModel.BulkRestoreSelected();
        private void BulkCopy_Click(object sender, RoutedEventArgs e) { this.ViewModel.CopySelectedToClipboard(); ShowCopiedFlyout(_lastPointerPosition); this.ViewModel.ClearSelection(); }
        private async void BulkDelete_Click(object sender, RoutedEventArgs e) => await this.ViewModel.BulkDeleteSelected();
        private void CancelSelection_Click(object sender, RoutedEventArgs e) => this.ViewModel.ClearSelection();
        private void FocusItem_Click(object sender, RoutedEventArgs e) { if (sender is FrameworkElement fe && fe.DataContext is MediaItem item) _ = OpenMedia(item, force: true); }
        /// <summary>Enables selection mode or opens the selection actions flyout.</summary>
        private void SelectBtn_Click(object sender, RoutedEventArgs e) { if (!this.ViewModel.IsSelectMode) this.ViewModel.IsSelectMode = true; else { MenuFlyout? flyout = this.RootGrid.Resources["SelectionMenu"] as MenuFlyout; if (sender is FrameworkElement fe && flyout != null) flyout.ShowAt(fe); } }

        /// <summary>Walks the visual tree upwards to find the MediaItem associated with a UI element.</summary>
        private MediaItem? GetMediaItemFromElement(object originalSource) { DependencyObject? depObj = originalSource as DependencyObject; while (depObj != null && depObj != ImageGridView) { if (depObj is FrameworkElement fe && fe.DataContext is MediaItem item) return item; depObj = VisualTreeHelper.GetParent(depObj); } return null; }

        /// <summary>Handles pointer press events on the media grid for selection, dragging, and context menus.</summary>
        private void ImageGridView_PointerPressed(object sender, PointerRoutedEventArgs e) {
            var pt = e.GetCurrentPoint(ImageGridView);
            if (pt.Position.Y < 32) return;
            bool isLeft = pt.Properties.IsLeftButtonPressed;
            bool isRight = pt.Properties.IsRightButtonPressed;

            if (isLeft || isRight) {
                MediaItem? clickedItem = GetMediaItemFromElement(e.OriginalSource);
                if (clickedItem != null) {
                    long currentTicks = DateTime.Now.Ticks;
                    bool isDoubleClick = isLeft && (currentTicks - _lastClickTicks < TimeSpan.TicksPerMillisecond * 500) && (Math.Abs(pt.Position.X - _lastClickPos.X) < 10 && Math.Abs(pt.Position.Y - _lastClickPos.Y) < 10);

                    if (isDoubleClick) _singleClickTimer.Stop();
                    _lastClickTicks = currentTicks; _lastClickPos = pt.Position;
                    var shiftPressed = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

                    if (this.ViewModel.IsSelectMode && shiftPressed && _lastSelectedAnchor != null) { this.ViewModel.SelectRange(_lastSelectedAnchor, clickedItem); e.Handled = true; }
                    else if (isDoubleClick) { _isDoubleClickDrag = false; _isDragSelecting = false; _doubleClickStartItem = clickedItem; _previousSelection = this.ViewModel.SelectedItems.ToList(); ImageGridView.CapturePointer(e.Pointer); }
                    else { _isDragSelecting = false; _isDoubleClickDrag = false; _isRightDrag = isRight; _dragSelectStartPos = pt.Position; _pressedItem = clickedItem; ImageGridView.CapturePointer(e.Pointer); }
                }
            }
        }

        /// <summary>Processes drag-selection of multiple items or double-click range selection while moving.</summary>
        private void ImageGridView_PointerMoved(object sender, PointerRoutedEventArgs e) {
            if (ImageGridView.PointerCaptures != null && ImageGridView.PointerCaptures.Count > 0) {
                var pt = e.GetCurrentPoint(ImageGridView);
                if (_doubleClickStartItem != null && !_isDoubleClickDrag && (Math.Abs(pt.Position.X - _lastClickPos.X) > 10 || Math.Abs(pt.Position.Y - _lastClickPos.Y) > 10)) _isDoubleClickDrag = true;
                if (!_isDragSelecting && !_isDoubleClickDrag && (Math.Abs(pt.Position.X - _dragSelectStartPos.X) > 10 || Math.Abs(pt.Position.Y - _dragSelectStartPos.Y) > 10)) _isDragSelecting = true;

                if (_isDoubleClickDrag && _doubleClickStartItem != null) {
                    var globalPt = e.GetCurrentPoint(null).Position;
                    var elements = VisualTreeHelper.FindElementsInHostCoordinates(globalPt, ImageGridView);
                    foreach (var element in elements) {
                        if (element is FrameworkElement fe && fe.DataContext is MediaItem item) {
                            if (!this.ViewModel.IsSelectMode) this.ViewModel.IsSelectMode = true;

                            var newRange = new HashSet<MediaItem>();
                            int idx1 = this.ViewModel.MediaItems.IndexOf(_doubleClickStartItem);
                            int idx2 = this.ViewModel.MediaItems.IndexOf(item);
                            if (idx1 != -1 && idx2 != -1) {
                                int low = Math.Min(idx1, idx2);
                                int high = Math.Max(idx1, idx2);
                                for (int i = low; i <= high; i++) newRange.Add(this.ViewModel.MediaItems[i]);
                            }

                            var currentSelected = this.ViewModel.SelectedItems.ToList();
                            foreach (var sel in currentSelected) if (!newRange.Contains(sel) && !_previousSelection.Contains(sel)) this.ViewModel.ToggleItemSelection(sel);

                            foreach (var rItem in newRange) if (!rItem.IsSelected) this.ViewModel.ToggleItemSelection(rItem);

                            foreach (var prev in _previousSelection) if (!prev.IsSelected) this.ViewModel.ToggleItemSelection(prev);

                            _lastSelectedAnchor = item;
                            break;
                        }
                    }
                }
                else if (_isDragSelecting) {
                    var globalPt = e.GetCurrentPoint(null).Position;
                    var elements = VisualTreeHelper.FindElementsInHostCoordinates(globalPt, ImageGridView);
                    foreach (var element in elements) {
                        if (element is FrameworkElement fe && fe.DataContext is MediaItem item) {
                            if (!this.ViewModel.IsSelectMode) this.ViewModel.IsSelectMode = true;
                            if (!item.IsSelected) { this.ViewModel.ToggleItemSelection(item); _lastSelectedAnchor = item; }
                            break;
                        }
                    }
                }
            }
        }

        /// <summary>Finalizes drag-selection, single clicks, or context menu triggers upon pointer release.</summary>
        private void ImageGridView_PointerReleased(object sender, PointerRoutedEventArgs e) {
            ImageGridView.ReleasePointerCapture(e.Pointer); MediaItem? releasedItem = GetMediaItemFromElement(e.OriginalSource); var pt = e.GetCurrentPoint(ImageGridView); _lastPointerPosition = pt.Position;
            if (_isDoubleClickDrag) { _isDoubleClickDrag = false; _doubleClickStartItem = null; }
            else if (_isDragSelecting) {
                _isDragSelecting = false;
                if (_isRightDrag && this.RootGrid.Resources["SelectionMenu"] is MenuFlyout flyout) { var restoreItem = flyout.Items.OfType<MenuFlyoutItem>().FirstOrDefault(i => i.Name == "BulkRestoreMenu"); if (restoreItem != null) restoreItem.Visibility = this.ViewModel.SelectedItems.Any(x => x.IsDeleted) ? Visibility.Visible : Visibility.Collapsed; flyout.ShowAt(ImageGridView, pt.Position); }
            }
            else if (releasedItem != null && (releasedItem == _pressedItem || releasedItem == _doubleClickStartItem)) {
                if (_isRightDrag) {
                    if (this.ViewModel.IsSelectMode && releasedItem.IsSelected) { if (this.RootGrid.Resources["SelectionMenu"] is MenuFlyout flyout) { var restoreItem = flyout.Items.OfType<MenuFlyoutItem>().FirstOrDefault(i => i.Name == "BulkRestoreMenu"); if (restoreItem != null) restoreItem.Visibility = this.ViewModel.SelectedItems.Any(x => x.IsDeleted) ? Visibility.Visible : Visibility.Collapsed; flyout.ShowAt(ImageGridView, pt.Position); } }
                    else { _rightClickedItem = releasedItem; if (this.RootGrid.Resources["SingleItemMenu"] is MenuFlyout flyout) { var restoreItem = flyout.Items.OfType<MenuFlyoutItem>().FirstOrDefault(i => i.Name == "SingleItemRestoreMenu"); if (restoreItem != null) restoreItem.Visibility = releasedItem.IsDeleted ? Visibility.Visible : Visibility.Collapsed; flyout.ShowAt(ImageGridView, pt.Position); } }
                }
                else if (this.ViewModel.IsSelectMode) { this.ViewModel.ToggleItemSelection(releasedItem); _lastSelectedAnchor = releasedItem; } else _ = OpenMedia(releasedItem);
            }
            _pressedItem = null; _doubleClickStartItem = null; _isRightDrag = false;
        }

#endregion
        #region Remaining Logic (Search, Tags, Scaling, Image Load, Flyout Actions)

        /// <summary>Enables filtering by a specific file extension.</summary>
        private void ExtensionFilter_Checked(object sender, RoutedEventArgs e) { if (sender is CheckBox cb && cb.Content is string s) ViewModel.ToggleExtensionFilter(s); }
        /// <summary>Disables filtering by a specific file extension.</summary>
        private void ExtensionFilter_Unchecked(object sender, RoutedEventArgs e) { if (sender is CheckBox cb && cb.Content is string s) ViewModel.ToggleExtensionFilter(s); }

        /// <summary>Animates the window dimensions to optimally match the aspect ratio of the currently loaded media item.</summary>
        /// <remarks>Complexity: O(1)</remarks>
        private void ScaleWindow_Click(object sender, RoutedEventArgs e) {
            if (this.ViewModel.SelectedItem == null) return;
            double aspectRatio = 0;
            if (this.ViewModel.SelectedItem.IsVideo && this.ViewerVideo.MediaPlayer != null) { var session = this.ViewerVideo.MediaPlayer.PlaybackSession; if (session.NaturalVideoHeight > 0) aspectRatio = (double)session.NaturalVideoWidth / session.NaturalVideoHeight; }
            else if (this.ViewerImage.Source is BitmapImage bmp && bmp.PixelHeight > 0) aspectRatio = (double)bmp.PixelWidth / bmp.PixelHeight;

            if (aspectRatio > 0) {
                double currentContentHeight = this.RootGrid.ActualHeight; double currentContentWidth = this.RootGrid.ActualWidth; double targetContentWidth = currentContentHeight * aspectRatio; double widthDelta = targetContentWidth - currentContentWidth;
                int currentWindowWidth = this.AppWindow.Size.Width; int currentWindowHeight = this.AppWindow.Size.Height; int targetWindowWidth = (int)Math.Round(currentWindowWidth + widthDelta); int targetWindowHeight = currentWindowHeight;
                if (targetWindowWidth < 400) { targetWindowWidth = 400; double clampedContentWidth = currentContentWidth + (targetWindowWidth - currentWindowWidth); double targetContentHeight = clampedContentWidth / aspectRatio; targetWindowHeight = (int)Math.Round(currentWindowHeight + (targetContentHeight - currentContentHeight)); }
                StartWindowResizeAnimation(targetWindowWidth, targetWindowHeight);
            }
        }

        /// <summary>Initiates a smooth window resizing animation.</summary>
        private void StartWindowResizeAnimation(int targetWidth, int? targetHeight = null) { _resizeStartWidth = this.AppWindow.Size.Width; _resizeStartHeight = this.AppWindow.Size.Height; _resizeTargetWidth = targetWidth; _resizeTargetHeight = targetHeight ?? _resizeStartHeight; _resizeStopwatch.Restart(); _resizeTimer.Start(); }
        /// <summary>Applies interpolated window dimensions for smooth resizing.</summary>
        private void ResizeTimer_Tick(object? sender, object e) { double progress = Math.Clamp(_resizeStopwatch.Elapsed.TotalMilliseconds / ResizeDurationMs, 0, 1); double eased = 1 - Math.Pow(1 - progress, 5); int cw = (int)(_resizeStartWidth + (_resizeTargetWidth - _resizeStartWidth) * eased); int ch = (int)(_resizeStartHeight + (_resizeTargetHeight - _resizeStartHeight) * eased); this.AppWindow.Resize(new SizeInt32(cw, ch)); if (progress >= 1) { _resizeTimer.Stop(); _resizeStopwatch.Stop(); } }

        /// <summary>Loads and displays the specified media item in the viewer overlay.</summary>
        /// <remarks>Complexity: O(1)</remarks>
        private async Task OpenMedia(MediaItem item, bool force = false) {
            if (this.ViewModel.IsSelectMode && !force) return;
            MainViewModel.IsViewerOpen = true;
            if (this.ViewerOverlay.Visibility != Visibility.Visible) _defaultLibraryWidth = this.AppWindow.Size.Width;
            this.ViewModel.SelectedItem = item; this.ViewerOverlay.Visibility = Visibility.Visible; this.MediaScrollViewer.ChangeView(0, 0, 1.0f, true);
            UpdateActionPanelLayout(); AnimateHud(true);
            if (item.IsVideo) { this.ViewerImage.Visibility = Visibility.Collapsed; this.ViewerVideo.Visibility = Visibility.Visible; try { StorageFile file = await StorageFile.GetFileFromPathAsync(item.FilePath); this.ViewerVideo.Source = MediaSource.CreateFromStorageFile(file); } catch { } } else { this.ViewerVideo.Visibility = Visibility.Collapsed; this.ViewerImage.Visibility = Visibility.Visible; this.ViewerVideo.Source = null!; try { StorageFile file = await StorageFile.GetFileFromPathAsync(item.FilePath); using var stream = await file.OpenAsync(FileAccessMode.Read); BitmapImage bmp = new BitmapImage(); await bmp.SetSourceAsync(stream); this.ViewerImage.Source = bmp; } catch { this.ViewerImage.Source = item.Thumbnail; } }
            this.UpdateViewerLayout();
        }

        /// <summary>Closes the full-screen media viewer and animates the window back to library view width.</summary>
        private void CloseViewer_Click(object? sender, RoutedEventArgs? e) {
            if (this.ViewModel.SelectedItem == null) return;

            MainViewModel.IsViewerOpen = false;

            this.ViewerOverlay.Visibility = Visibility.Collapsed;
            this.ViewerImage.Source = null!;
            this.ViewerVideo.Source = null!;
            this.ViewModel.SelectedItem = null;
            StartWindowResizeAnimation(_defaultLibraryWidth, this.AppWindow.Size.Height);
            StopIdleTimer();
            AnimateHud(true);
        }

        /// <summary>Displays a detailed info dialog containing file metadata (size, resolution, path).</summary>
        private async void InfoBtn_Click(object sender, RoutedEventArgs e) {
            if (this.ViewModel.SelectedItem == null) return;
            var item = this.ViewModel.SelectedItem; StorageFile file = await StorageFile.GetFileFromPathAsync(item.FilePath); var props = await file.GetBasicPropertiesAsync();
            string res = "Unknown";
            if (item.IsVideo) { var v = await file.Properties.GetVideoPropertiesAsync(); res = $"{v.Width} x {v.Height}"; } else { var i = await file.Properties.GetImagePropertiesAsync(); res = $"{i.Width} x {i.Height}"; }
            var stack = new StackPanel { Spacing = 8 };
            stack.Children.Add(new TextBlock { Text = $"Name: {file.Name}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            stack.Children.Add(new TextBlock { Text = $"Location: {file.Path}", TextWrapping = TextWrapping.Wrap, Opacity = 0.7 });
            stack.Children.Add(new TextBlock { Text = $"Size: {props.Size / 1024.0 / 1024.0:F2} MB" });
            stack.Children.Add(new TextBlock { Text = $"Resolution: {res}" });
            stack.Children.Add(new TextBlock { Text = $"Extension: {item.Extension}" });
            var dialog = new ContentDialog { Title = "Details", Content = stack, CloseButtonText = "Close", PrimaryButtonText = "Open File Location", XamlRoot = this.RootGrid.XamlRoot };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary) { await Launcher.LaunchFolderAsync(await file.GetParentAsync(), new FolderLauncherOptions { ItemsToSelect = { file } }); }
        }

        /// <summary>Navigates left or right in the media viewer.</summary>
        private async Task NavigateMedia(int direction) { int index = this.ViewModel.GetNextIndex(direction); if (index != -1) await OpenMedia(this.ViewModel.MediaItems[index]); }
        /// <summary>Creates a new tag and optionally auto-tags all items with matching filenames.</summary>
        private async void CreateTag_Click(object sender, RoutedEventArgs e) { var t = new TextBox { PlaceholderText = "Tag name", Width = 300 }; var autoTagCheck = new CheckBox { Content = "Auto-tag files sharing this name", Margin = new Thickness(0, 8, 0, 0) }; var stack = new StackPanel(); stack.Children.Add(t); stack.Children.Add(autoTagCheck); if (await new ContentDialog { Title = "New Tag", Content = stack, PrimaryButtonText = "Create", CloseButtonText = "Cancel", XamlRoot = this.RootGrid.XamlRoot }.ShowAsync() == ContentDialogResult.Primary) { string tagName = t.Text.Trim(); this.ViewModel.AddNewTag(tagName); if (autoTagCheck.IsChecked == true) await this.ViewModel.AutoTagByFilename(tagName); } }

        /// <summary>Animates the search box expansion upon receiving focus.</summary>
        private void MediaSearchBox_GotFocus(object sender, RoutedEventArgs e) { AnimateSearchBoxWidth(200); AnimateSearchIconOpacity(0); }
        /// <summary>Animates the search box collapse upon losing focus when empty.</summary>
        private void MediaSearchBox_LostFocus(object sender, RoutedEventArgs e) { if (string.IsNullOrWhiteSpace(MediaSearchBox.Text)) { AnimateSearchBoxWidth(48); AnimateSearchIconOpacity(0.7); } }
        /// <summary>Animates the width of the search box for smooth focus expansion.</summary>
        private void AnimateSearchBoxWidth(double targetWidth) { var sb = new Storyboard(); var anim = new DoubleAnimation { To = targetWidth, Duration = TimeSpan.FromMilliseconds(250), EnableDependentAnimation = true, EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 } }; Storyboard.SetTarget(anim, MediaSearchBox); Storyboard.SetTargetProperty(anim, "Width"); sb.Children.Add(anim); sb.Begin(); }
        /// <summary>Animates the opacity of the search icon when the search box receives or loses focus.</summary>
        private void AnimateSearchIconOpacity(double targetOpacity) { var sb = new Storyboard(); var anim = new DoubleAnimation { To = targetOpacity, Duration = TimeSpan.FromMilliseconds(150), EasingFunction = new ExponentialEase { EasingMode = EasingMode.EaseOut, Exponent = 6 } }; Storyboard.SetTarget(anim, SearchIcon); Storyboard.SetTargetProperty(anim, "Opacity"); sb.Children.Add(anim); sb.Begin(); }
        /// <summary>Initiates panning in the media scroll viewer.</summary>
        private void MediaScrollViewer_PointerPressed(object sender, PointerRoutedEventArgs e) { if (this.MediaScrollViewer.ZoomFactor <= 1.0) return; var pt = e.GetCurrentPoint(this.MediaScrollViewer); if (pt.Properties.IsLeftButtonPressed) { this._isDragging = true; this._lastPointerPosition = pt.Position; this.MediaScrollViewer.CapturePointer(e.Pointer); } }
        /// <summary>Pans the media scroll viewer during dragging.</summary>
        private void MediaScrollViewer_PointerMoved(object sender, PointerRoutedEventArgs e) { if (!this._isDragging) return; var pt = e.GetCurrentPoint(this.MediaScrollViewer); this.MediaScrollViewer.ChangeView(this.MediaScrollViewer.HorizontalOffset - (pt.Position.X - this._lastPointerPosition.X), this.MediaScrollViewer.VerticalOffset - (pt.Position.Y - this._lastPointerPosition.Y), (float?)null, true); this._lastPointerPosition = pt.Position; }
        /// <summary>Ends panning in the media scroll viewer.</summary>
        private void MediaScrollViewer_PointerReleased(object sender, PointerRoutedEventArgs e) { this._isDragging = false; this.MediaScrollViewer.ReleasePointerCapture(e.Pointer); }
        /// <summary>Opens a media item from the active selection list.</summary>
        private void SelectedItemsList_ItemClick(object sender, ItemClickEventArgs e) { if (e.ClickedItem is MediaItem item) _ = OpenMedia(item, force: true); }
        /// <summary>Toggles selection for the clicked media item.</summary>
        private void DeselectItem_Click(object sender, RoutedEventArgs e) { if (sender is FrameworkElement fe && fe.DataContext is MediaItem item) this.ViewModel.ToggleItemSelection(item); }
        /// <summary>Filters the media grid by the selected tag.</summary>
        private void TagListView_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (this.TagListView.SelectedItem is TagItem tag) this.ViewModel.FilterByTag(tag.Name); }
        /// <summary>Filters the media grid dynamically as search text changes.</summary>
        private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => this.ViewModel.FilterBySearch(sender.Text);
        /// <summary>Updates view width constraints upon resizing the content grid.</summary>
        private void ContentGrid_SizeChanged(object sender, SizeChangedEventArgs e) => this.ViewModel.UpdateGridWidth(e.NewSize.Width);
        /// <summary>Adjusts photos per row when Ctrl+Scrolling on the grid.</summary>
        private void ContentGrid_PointerWheelChanged(object sender, PointerRoutedEventArgs e) { if (e.KeyModifiers.HasFlag(VirtualKeyModifiers.Control)) { var ptr = e.GetCurrentPoint(ContentGrid); this.ViewModel.AdjustPhotosPerRow(ptr.Properties.MouseWheelDelta > 0 ? 1 : -1); e.Handled = true; } }
        /// <summary>Adjusts photos per row via touch pinch/zoom manipulation.</summary>
        private void ContentGrid_ManipulationDelta(object sender, ManipulationDeltaRoutedEventArgs e) { if (e.Delta.Scale > 1.1) this.ViewModel.AdjustPhotosPerRow(1); else if (e.Delta.Scale < 0.9) this.ViewModel.AdjustPhotosPerRow(-1); }
        /// <summary>Sets the explicit number of photos per row from the scale menu.</summary>
        private void ScaleItem_Click(object sender, RoutedEventArgs e) { if (sender is MenuFlyoutItem item && int.TryParse(item.Tag?.ToString(), out int n)) this.ViewModel.PhotosPerRow = n; }
        /// <summary>Toggles the favorite status for the current selection.</summary>
        private void FavBtn_Click(object sender, RoutedEventArgs e) => this.ViewModel.ToggleTagForSelection("Favorites");

        /// <summary>Opens a flyout menu to toggle multiple tags for the selected item.</summary>
        private void TagBtn_Click(object sender, RoutedEventArgs e) {
            var f = new MenuFlyout();
            foreach (var tag in this.ViewModel.Tags) { if (tag.Name == "All Photos") continue; var i = new ToggleMenuFlyoutItem { Text = tag.Name, IsChecked = this.ViewModel.SelectedItem?.Tags.Contains(tag.Name) ?? false }; i.Click += (s, a) => this.ViewModel.ToggleTagForSelection(tag.Name); f.Items.Add(i); }
            if (sender is Button btn) { btn.Flyout = f; f.ShowAt(btn); } else if (sender is FrameworkElement fe) f.ShowAt(fe);
        }

        /// <summary>Renames a tag via a prompt dialog.</summary>
        private async void RenameTag_Click(object sender, RoutedEventArgs e) { if ((sender as MenuFlyoutItem)?.DataContext is TagItem t) { var tb = new TextBox { Text = t.Name }; if (await new ContentDialog { Title = "Rename", Content = tb, PrimaryButtonText = "Rename", CloseButtonText = "Cancel", XamlRoot = this.RootGrid.XamlRoot }.ShowAsync() == ContentDialogResult.Primary) this.ViewModel.RenameTag(t, tb.Text); } }
        /// <summary>Deletes a tag from the database.</summary>
        private void DeleteTag_Click(object sender, RoutedEventArgs e) { if ((sender as MenuFlyoutItem)?.DataContext is TagItem t) this.ViewModel.DeleteTag(t); }
        /// <summary>Moves the currently viewed item to the Recycle Bin and loads the next item.</summary>
        private async void DeleteBtn_Click(object sender, RoutedEventArgs e) { if (this.ViewModel.SelectedItem == null) return; int c = this.ViewModel.MediaItems.IndexOf(this.ViewModel.SelectedItem); if (await this.ViewModel.DeleteCurrentItem()) { await Task.Delay(10); if (this.ViewModel.MediaItems.Count > 0) await OpenMedia(this.ViewModel.MediaItems[Math.Clamp(c, 0, this.ViewModel.MediaItems.Count - 1)]); else CloseViewer_Click(null, null); } }
        /// <summary>Opens the file explorer at the physical location of the currently viewed item.</summary>
        private async void LocationBtn_Click(object sender, RoutedEventArgs e) { if (this.ViewModel.SelectedItem != null) { StorageFile f = await StorageFile.GetFileFromPathAsync(this.ViewModel.SelectedItem.FilePath); await Launcher.LaunchFolderAsync(await f.GetParentAsync(), new FolderLauncherOptions { ItemsToSelect = { f } }); } }
        /// <summary>Opens the currently viewed item in the default system application.</summary>
        private async void EditBtn_Click(object sender, RoutedEventArgs e) { if (this.ViewModel.SelectedItem != null) await Launcher.LaunchFileAsync(await StorageFile.GetFileFromPathAsync(this.ViewModel.SelectedItem.FilePath), new LauncherOptions { DisplayApplicationPicker = true }); }
        /// <summary>Navigates to the previous item in the viewer.</summary>
        private async void PreviousBtn_Click(object sender, RoutedEventArgs e) => await NavigateMedia(-1);
        /// <summary>Navigates to the next item in the viewer.</summary>
        private async void NextBtn_Click(object sender, RoutedEventArgs e) => await NavigateMedia(1);
        /// <summary>Updates alignment constraints for the viewer overlay to center content properly.</summary>
        private void UpdateViewerLayout() { if (this.ViewerOverlay.Visibility != Visibility.Visible) return; this.ViewerImage.MaxWidth = this.ViewerVideo.MaxWidth = this.MediaScrollViewer.ActualWidth; this.ViewerImage.MaxHeight = this.ViewerVideo.MaxHeight = this.MediaScrollViewer.ActualHeight; this.CenteringGrid.MinWidth = this.MediaScrollViewer.ActualWidth; this.CenteringGrid.MinHeight = this.MediaScrollViewer.ActualHeight; }
        /// <summary>Recomputes viewer layout when the scroll viewer resizes.</summary>
        private void MediaScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e) => this.UpdateViewerLayout();

#endregion
    }
}
