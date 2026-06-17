using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Search;
using Microsoft.UI.Xaml;
using System.Text.Json;
using System.IO;
using Windows.ApplicationModel.DataTransfer;

namespace CocosGallery
{
    public class TagItem : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private int _count;
        private bool? _isCheckedForEdit = false;
        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public int Count { get => _count; set => SetProperty(ref _count, value); }
        public string DisplayName => $"{Name} ({Count})";
        public bool IsEditable { get; set; } = true;
        public Visibility IsEditableVisibility => IsEditable ? Visibility.Visible : Visibility.Collapsed;
        public Visibility IsVisibleInEditMenu => (IsEditable || Name == "Favorites") ? Visibility.Visible : Visibility.Collapsed;
        public bool? IsCheckedForEdit { get => _isCheckedForEdit; set => SetProperty(ref _isCheckedForEdit, value); }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value)) return;
            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            if (propertyName == nameof(Name) || propertyName == nameof(Count))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));
        }
    }

    public class MetadataEntry
    {
        public bool IsFavorite { get; set; }
        public bool IsDeleted { get; set; }
        public HashSet<string> Tags { get; set; } = new();
    }

    public class MainViewModel : INotifyPropertyChanged
    {
        public static Dictionary<string, MetadataEntry> MetadataStore = new();
        public static bool GlobalIsSelectMode { get; set; } = false;

        public static bool IsViewerOpen { get; set; } = false;

        public static ObservableCollection<MediaItem> GlobalSelectedItems { get; } = new();

        private string MetadataFilePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "gallery_metadata.json");

        public ObservableCollection<TagItem> Tags { get; } = new();
        public ObservableCollection<FolderItem> Folders { get; } = new();
        public ObservableCollection<string> AvailableExtensions { get; } = new();
        public HashSet<string> ActiveExtensions { get; } = new();
        public IncrementalMediaSource MediaItems { get; private set; } = new();

        public static bool IsCrossMemoryEnabled
        {
            get => ApplicationData.Current.LocalSettings.Values["CrossMemory"] as bool? ?? false;
            set => ApplicationData.Current.LocalSettings.Values["CrossMemory"] = value;
        }

        public ObservableCollection<MediaItem> SelectedItems { get; }

        public Visibility HasSelectionVisibility => SelectedItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public int SelectedCount => SelectedItems.Count;

        private FolderItem? _selectedFolder;
        public FolderItem? SelectedFolder
        {
            get => _selectedFolder;
            set
            {
                if (SetProperty(ref _selectedFolder, value))
                {
                    RefreshMediaItems();
                }
            }
        }

        private MediaItem? _selectedItem;
        public MediaItem? SelectedItem { get => _selectedItem; set => SetProperty(ref _selectedItem, value); }

        private bool _isSidebarVisible = false;
        public bool IsSidebarVisible { get => _isSidebarVisible; set => SetProperty(ref _isSidebarVisible, value); }

        private bool _isAutoShrinkSidebar = true;
        public bool IsAutoShrinkSidebar
        {
            get => _isAutoShrinkSidebar;
            set
            {
                if (SetProperty(ref _isAutoShrinkSidebar, value))
                {
                    ApplicationData.Current.LocalSettings.Values["AutoShrink"] = value;
                }
            }
        }

        private bool _isPreferContextMenus = false;
        public bool IsPreferContextMenus
        {
            get => _isPreferContextMenus;
            set { if (SetProperty(ref _isPreferContextMenus, value)) ApplicationData.Current.LocalSettings.Values["PreferContextMenus"] = value; }
        }

        private bool _isRunInBackgroundEnabled = true;
        public bool IsRunInBackgroundEnabled
        {
            get => _isRunInBackgroundEnabled;
            set 
            { 
                if (SetProperty(ref _isRunInBackgroundEnabled, value)) 
                {
                    ApplicationData.Current.LocalSettings.Values["RunInBackground"] = value;
                    App.Instance?.UpdateTrayVisibility(value);
                }
            }
        }

        private bool _isWipeCacheOnClose = false;
        public bool IsWipeCacheOnClose
        {
            get => _isWipeCacheOnClose;
            set { if (SetProperty(ref _isWipeCacheOnClose, value)) ApplicationData.Current.LocalSettings.Values["WipeCacheOnClose"] = value; }
        }

        private bool _areActionButtonsVisible = true;
        public bool AreActionButtonsVisible { get => _areActionButtonsVisible; set => SetProperty(ref _areActionButtonsVisible, value); }

        private bool _isSelectMode = false;
        public bool IsSelectMode
        {
            get => _isSelectMode;
            set
            {
                if (SetProperty(ref _isSelectMode, value))
                {
                    if (!value) ClearSelection();
                    if (IsCrossMemoryEnabled) GlobalIsSelectMode = value;
                    foreach (var item in MediaItems) item.IsSelectModeActive = value;
                }
            }
        }

        private int _photosPerRow = 6;
        public int PhotosPerRow
        {
            get => _photosPerRow;
            set { if (SetProperty(ref _photosPerRow, value)) RecalculateItemSize(); }
        }

        private double _calculatedItemSize = 150;
        public double CalculatedItemSize { get => _calculatedItemSize; set => SetProperty(ref _calculatedItemSize, value); }
        private double _currentGridWidth = 0;

        private string? _currentTagFilter = null;
        public string? CurrentTagFilter => _currentTagFilter;
        private string? _currentSearchQuery = null;

        public MainViewModel()
        {
            if (IsCrossMemoryEnabled)
            {
                SelectedItems = GlobalSelectedItems;
                if (GlobalIsSelectMode)
                {
                    _isSelectMode = true;
                }
            }
            else
            {
                SelectedItems = new ObservableCollection<MediaItem>();
            }

            SelectedItems.CollectionChanged += (s, e) => {
                OnPropertyChanged(nameof(SelectedCount));
                OnPropertyChanged(nameof(HasSelectionVisibility));
                UpdateSelectionIndices();

                if (IsCrossMemoryEnabled && s == GlobalSelectedItems)
                {
                    SyncLocalItemsWithGlobal();
                }
            };

            if (ApplicationData.Current.LocalSettings.Values.TryGetValue("AutoShrink", out object stored) && stored is bool val) _isAutoShrinkSidebar = val;
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue("PreferContextMenus", out object pcm) && pcm is bool pcmVal) _isPreferContextMenus = pcmVal;
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue("WipeCacheOnClose", out object wcc) && wcc is bool wccVal) _isWipeCacheOnClose = wccVal;
            if (ApplicationData.Current.LocalSettings.Values.TryGetValue("RunInBackground", out object rib) && rib is bool ribVal) _isRunInBackgroundEnabled = ribVal;

            Tags.Add(new TagItem { Name = "All Photos", Count = 0, IsEditable = false });
            Tags.Add(new TagItem { Name = "Favorites", Count = 0, IsEditable = false });
            Tags.Add(new TagItem { Name = "Recycle Bin", Count = 0, IsEditable = false });

            Folders.Add(new FolderItem { Name = "Pictures", Path = KnownFolders.PicturesLibrary.Path, IsSpecial = true });

            if (ApplicationData.Current.LocalSettings.Values.TryGetValue("CustomFolders", out object fObj) && fObj is string fJson)
            {
                try
                {
                    using var doc = JsonDocument.Parse(fJson);
                    if (doc.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in doc.RootElement.EnumerateArray())
                        {
                            if (element.ValueKind == JsonValueKind.String)
                            {
                                string p = element.GetString() ?? string.Empty;
                                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) Folders.Add(new FolderItem { Name = Path.GetFileName(p), Path = p });
                            }
                            else
                            {
                                string n = element.GetProperty("Name").GetString() ?? string.Empty;
                                string p = element.GetProperty("Path").GetString() ?? string.Empty;
                                if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) Folders.Add(new FolderItem { Name = n, Path = p });
                            }
                        }
                    }
                }
                catch { }
            }

            SelectedFolder = Folders.First();
            _ = InitializeAsync();
        }

        public void SaveFolders()
        {
            var customFolders = Folders.Where(f => !f.IsSpecial).Select(f => new { f.Name, f.Path }).ToList();
            ApplicationData.Current.LocalSettings.Values["CustomFolders"] = JsonSerializer.Serialize(customFolders);
        }

        public void RemoveFolder(FolderItem folder)
        {
            if (folder.IsSpecial) return;
            Folders.Remove(folder);
            SaveFolders();
            if (SelectedFolder == folder) SelectedFolder = Folders.First();
        }

        private async Task InitializeAsync()
        {
            await LoadMetadataAsync();
            _ = ScanAvailableExtensionsAsync();
            await UpdateAllPhotosCountAsync();
            await CleanupStaleMetadataAsync();
            UpdateTagCounts();
            RefreshMediaItems();
        }

        private async Task ScanAvailableExtensionsAsync()
        {
            try
            {
                var options = new QueryOptions(CommonFileQuery.OrderByDate, new[] { ".jpg", ".png", ".jpeg", ".dng", ".mkv", ".mp4", ".mov" }) { FolderDepth = FolderDepth.Deep };
                StorageFolder target = KnownFolders.PicturesLibrary;
                if (SelectedFolder != null && Directory.Exists(SelectedFolder.Path)) target = await StorageFolder.GetFolderFromPathAsync(SelectedFolder.Path);

                var query = target.CreateFileQueryWithOptions(options);
                var files = await query.GetFilesAsync(0, 500);
                var exts = files.Select(f => f.FileType.ToLower()).Distinct().OrderBy(e => e).ToList();

                var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                dispatcher?.TryEnqueue(() => {
                    AvailableExtensions.Clear();
                    foreach (var ext in exts) AvailableExtensions.Add(ext);
                });
            }
            catch { }
        }

        public void UpdateGridWidth(double width) { _currentGridWidth = width; RecalculateItemSize(); }
        private void RecalculateItemSize() { if (_currentGridWidth <= 0) return; CalculatedItemSize = _currentGridWidth / PhotosPerRow; }
        public void AdjustPhotosPerRow(int delta) { int newValue = _photosPerRow - delta; if (newValue >= 1 && newValue <= 20) PhotosPerRow = newValue; }
        public void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;
        public void ToggleActionButtons() => AreActionButtonsVisible = !AreActionButtonsVisible;

        public void ToggleItemSelection(MediaItem item)
        {
            if (item.IsSelected)
            {
                item.IsSelected = false;
                SelectedItems.Remove(item);
                item.SelectionIndex = 0;
            }
            else
            {
                item.IsSelected = true;
                if (!SelectedItems.Contains(item))
                    SelectedItems.Add(item);
            }
        }

        public void UpdateSelectionIndices()
        {
            for (int i = 0; i < SelectedItems.Count; i++)
            {
                SelectedItems[i].SelectionIndex = i + 1;
            }
            if (IsCrossMemoryEnabled) SyncLocalItemsWithGlobal();
        }

        // pre: cross-memory selection is enabled, and global selection has changed
        // post: local media items update their selection state to match global
        // except: silences exceptions if items are modified concurrently
        // complexity: O(n) where n is local media items
        private void SyncLocalItemsWithGlobal()
        {
            var globalDict = SelectedItems.ToDictionary(i => i.FilePath, i => i.SelectionIndex);
            foreach (var item in MediaItems)
            {
                if (globalDict.TryGetValue(item.FilePath, out int idx))
                {
                    if (!item.IsSelected) item.IsSelected = true;
                    item.SelectionIndex = idx;
                }
                else
                {
                    if (item.IsSelected) item.IsSelected = false;
                    item.SelectionIndex = 0;
                }
            }
        }

        public void ClearSelection()
        {
            foreach (var item in SelectedItems.ToList())
            {
                item.IsSelected = false;
                item.SelectionIndex = 0;
            }
            SelectedItems.Clear();
            IsSelectMode = false;
        }

        public void SelectRange(MediaItem start, MediaItem end)
        {
            int idx1 = MediaItems.IndexOf(start), idx2 = MediaItems.IndexOf(end);
            if (idx1 == -1 || idx2 == -1) return;
            int low = Math.Min(idx1, idx2), high = Math.Max(idx1, idx2);
            for (int i = low; i <= high; i++)
            {
                if (!MediaItems[i].IsSelected)
                {
                    MediaItems[i].IsSelected = true;
                    SelectedItems.Add(MediaItems[i]);
                }
            }
        }

        // pre: global selected items contain valid files
        // post: selected items are renamed sequentially based on baseName
        // except: gracefully handles renaming errors per file
        // complexity: O(n) where n is the number of selected items
        public async Task BulkRenameItems(string baseName)
        {
            var itemsToRename = SelectedItems.ToList();
            if (itemsToRename.Count == 0) return;

            string dirPath = Path.GetDirectoryName(itemsToRename.First().FilePath) ?? string.Empty;
            int maxSeen = -1;

            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(dirPath);
                var files = await folder.GetFilesAsync();
                foreach (var f in files)
                {
                    string nameNoExt = Path.GetFileNameWithoutExtension(f.Name);
                    if (nameNoExt.Equals(baseName, StringComparison.OrdinalIgnoreCase)) maxSeen = Math.Max(maxSeen, 0);
                    else if (nameNoExt.StartsWith(baseName + " (", StringComparison.OrdinalIgnoreCase) && nameNoExt.EndsWith(")"))
                    {
                        string numStr = nameNoExt.Substring(baseName.Length + 2, nameNoExt.Length - baseName.Length - 3);
                        if (int.TryParse(numStr, out int c)) maxSeen = Math.Max(maxSeen, c);
                    }
                }
            }
            catch { }

            foreach (var item in itemsToRename)
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(item.FilePath);
                    string ext = file.FileType;
                    maxSeen++;

                    string newNameNoExt = maxSeen == 0 ? baseName : $"{baseName} ({maxSeen})";
                    string newName = newNameNoExt + ext;

                    await file.RenameAsync(newName, NameCollisionOption.GenerateUniqueName);

                    string oldPath = item.FilePath;
                    item.Title = file.Name;
                    item.FilePath = file.Path;

                    if (MetadataStore.TryGetValue(oldPath, out var existingMeta))
                    {
                        MetadataStore.Remove(oldPath);
                        MetadataStore[item.FilePath] = existingMeta;
                    }
                }
                catch { }
            }

            ClearSelection();
            _ = SaveMetadataAsync();
            RefreshMediaItems();
        }

        public void BulkAddTags(List<string> tagsToAdd)
        {
            if (tagsToAdd.Count == 0) return;
            foreach (var item in SelectedItems)
            {
                if (!MetadataStore.ContainsKey(item.FilePath)) MetadataStore[item.FilePath] = new MetadataEntry();
                var meta = MetadataStore[item.FilePath];

                foreach (var t in tagsToAdd)
                {
                    meta.Tags.Add(t);
                    item.Tags.Add(t);
                }
            }
            UpdateTagCounts();
            _ = SaveMetadataAsync();
        }

        public void BulkRemoveTags(List<string> tagsToRemove)
        {
            if (tagsToRemove.Count == 0) return;
            foreach (var item in SelectedItems)
            {
                if (MetadataStore.TryGetValue(item.FilePath, out var meta))
                {
                    foreach (var t in tagsToRemove)
                    {
                        meta.Tags.Remove(t);
                        item.Tags.Remove(t);
                    }
                }
            }
            UpdateTagCounts();
            _ = SaveMetadataAsync();
        }

        // pre: selected folder is valid and not null
        // post: tags are appended to metadata entries for matching filenames
        // except: ignores missing folders or read errors silently
        // complexity: O(files) to enumerate directory, O(1) per tag insertion
        public async Task AutoTagByFilename(string tagName)
        {
            if (SelectedFolder == null) return;
            try
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(SelectedFolder.Path);
                var files = await folder.GetFilesAsync();
                bool changed = false;

                foreach (var f in files)
                {
                    if (f.Name.Contains(tagName, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!MetadataStore.ContainsKey(f.Path)) MetadataStore[f.Path] = new MetadataEntry();
                        MetadataStore[f.Path].Tags.Add(tagName);
                        changed = true;
                    }
                }

                if (changed)
                {
                    UpdateTagCounts();
                    _ = SaveMetadataAsync();
                    RefreshMediaItems();
                }
            }
            catch { }
        }

        public void BulkEditTags(List<MediaItem> items, List<string> tagsToAdd, List<string> tagsToRemove)
        {
            if (items == null || items.Count == 0) return;
            bool addFav = tagsToAdd.Contains("Favorites");
            bool remFav = tagsToRemove.Contains("Favorites");
            foreach (var item in items)
            {
                if (addFav) item.IsFavorite = true;
                if (remFav) item.IsFavorite = false;
                foreach (var add in tagsToAdd) { if (add != "Favorites") item.Tags.Add(add); }
                foreach (var rem in tagsToRemove) { if (rem != "Favorites") item.Tags.Remove(rem); }
                UpdateMetadataStore(item);
            }
            UpdateTagCounts();
            _ = SaveMetadataAsync();
        }

        public async Task BulkDeleteSelected()
        {
            var selected = SelectedItems.ToList();
            foreach (var item in selected)
            {
                try
                {
                    string path = item.FilePath;
                    if (!item.IsDeleted)
                    {
                        item.IsDeleted = true;
                        UpdateMetadataStore(item);
                    }
                    else
                    {
                        var file = await StorageFile.GetFileFromPathAsync(path);
                        await file.DeleteAsync();
                        MediaItems.Remove(item);
                        MetadataStore.Remove(path);
                    }
                }
                catch { }
            }
            ClearSelection();
            RefreshMediaItems();
            UpdateTagCounts();
            _ = UpdateAllPhotosCountAsync();
            _ = SaveMetadataAsync();
        }

        public async Task BulkRestoreSelected()
        {
            var selected = SelectedItems.ToList();
            foreach (var item in selected)
            {
                if (item.IsDeleted)
                {
                    item.IsDeleted = false;
                    UpdateMetadataStore(item);
                }
            }
            ClearSelection();
            RefreshMediaItems();
            UpdateTagCounts();
            _ = UpdateAllPhotosCountAsync();
            _ = SaveMetadataAsync();
        }

        public async Task RestoreItem(MediaItem item)
        {
            if (item != null && item.IsDeleted)
            {
                item.IsDeleted = false;
                UpdateMetadataStore(item);
                RefreshMediaItems();
                UpdateTagCounts();
                _ = UpdateAllPhotosCountAsync();
                _ = SaveMetadataAsync();
            }
        }

        public void CopySelectedToClipboard()
        {
            var selectedFiles = SelectedItems.Select(i => i.FilePath).ToList();
            if (selectedFiles.Count == 0) return;
            var dataPackage = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            _ = Task.Run(async () => {
                var storageItems = new List<IStorageItem>();
                foreach (var path in selectedFiles) { try { storageItems.Add(await StorageFile.GetFileFromPathAsync(path)); } catch { } }
                dataPackage.SetStorageItems(storageItems);
                Clipboard.SetContent(dataPackage);
            });
        }

        public void ToggleExtensionFilter(string ext) { if (ActiveExtensions.Contains(ext)) ActiveExtensions.Remove(ext); else ActiveExtensions.Add(ext); RefreshMediaItems(); }
        public void FilterByTag(string? tagName) { _currentTagFilter = (tagName == "All Photos") ? null : tagName; RefreshMediaItems(); }
        public void FilterBySearch(string? query) { _currentSearchQuery = string.IsNullOrWhiteSpace(query) ? null : query.ToLower(); RefreshMediaItems(); }

        public void RefreshMediaItems() { MediaItems = new IncrementalMediaSource(SelectedFolder?.Path, _currentTagFilter, _currentSearchQuery, ActiveExtensions); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MediaItems))); }

        // pre: selected item is valid and not null
        // post: selected item is marked as deleted or permanently deleted from file system
        // except: returns false if deletion fails
        // complexity: O(1) mostly, or O(tags) to update metadata counts
        public async Task<bool> DeleteCurrentItem()
        {
            if (SelectedItem == null) return false;
            string path = SelectedItem.FilePath;
            if (!SelectedItem.IsDeleted) { SelectedItem.IsDeleted = true; UpdateMetadataStore(SelectedItem); RefreshMediaItems(); UpdateTagCounts(); _ = UpdateAllPhotosCountAsync(); _ = SaveMetadataAsync(); return true; }
            try { var file = await StorageFile.GetFileFromPathAsync(path); await file.DeleteAsync(); } catch { return false; }
            MediaItems.Remove(SelectedItem); MetadataStore.Remove(path); _ = SaveMetadataAsync(); UpdateTagCounts(); _ = UpdateAllPhotosCountAsync(); return true;
        }

        public void ToggleTagForSelection(string tagName) { if (SelectedItem == null || string.IsNullOrEmpty(tagName)) return; if (tagName == "Favorites") SelectedItem.IsFavorite = !SelectedItem.IsFavorite; else { if (SelectedItem.Tags.Contains(tagName)) SelectedItem.Tags.Remove(tagName); else SelectedItem.Tags.Add(tagName); } UpdateMetadataStore(SelectedItem); UpdateTagCounts(); _ = SaveMetadataAsync(); }
        public void AddNewTag(string name) { if (!string.IsNullOrEmpty(name) && !Tags.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase))) Tags.Add(new TagItem { Name = name, Count = 0 }); }
        public void DeleteTag(TagItem tag) { if (tag == null || !tag.IsEditable) return; foreach (var entry in MetadataStore.Values) entry.Tags.Remove(tag.Name); Tags.Remove(tag); _ = SaveMetadataAsync(); UpdateTagCounts(); }
        public void RenameTag(TagItem tag, string newName) { if (tag == null || !tag.IsEditable || string.IsNullOrEmpty(newName)) return; string oldName = tag.Name; tag.Name = newName; foreach (var entry in MetadataStore.Values) { if (entry.Tags.Contains(oldName)) { entry.Tags.Remove(oldName); entry.Tags.Add(newName); } } _ = SaveMetadataAsync(); }

        public int GetNextIndex(int direction) { if (SelectedItem == null || MediaItems.Count == 0) return -1; int currentIndex = MediaItems.IndexOf(SelectedItem); return (currentIndex + direction + MediaItems.Count) % MediaItems.Count; }

        private void UpdateMetadataStore(MediaItem item) { if (!MetadataStore.ContainsKey(item.FilePath)) MetadataStore[item.FilePath] = new MetadataEntry { IsFavorite = item.IsFavorite, IsDeleted = item.IsDeleted, Tags = new HashSet<string>(item.Tags) }; else { var entry = MetadataStore[item.FilePath]; entry.IsFavorite = item.IsFavorite; entry.IsDeleted = item.IsDeleted; entry.Tags = new HashSet<string>(item.Tags); } }
        public void UpdateTagCounts()
        {
            foreach (var t in Tags.Where(t => t.Name != "All Photos"))
            {
                t.Count = 0;
            }
            
            foreach (var entry in MetadataStore.Values)
            {
                if (entry.IsDeleted)
                {
                    var bin = Tags.FirstOrDefault(t => t.Name == "Recycle Bin");
                    if (bin != null) bin.Count++;
                    continue;
                }
                
                if (entry.IsFavorite)
                {
                    var fav = Tags.FirstOrDefault(t => t.Name == "Favorites");
                    if (fav != null) fav.Count++;
                }
                
                foreach (var tag in entry.Tags)
                {
                    var t = Tags.FirstOrDefault(x => x.Name == tag);
                    if (t != null) t.Count++;
                }
            }
        }
        private async Task UpdateAllPhotosCountAsync() { try { var queryOptions = new QueryOptions(CommonFileQuery.OrderByDate, new[] { ".jpg", ".png", ".mp4", ".mov" }); var query = KnownFolders.PicturesLibrary.CreateFileQueryWithOptions(queryOptions); var count = await query.GetItemCountAsync(); int deletedCount = MetadataStore.Values.Count(v => v.IsDeleted); var allPhotosTag = Tags.FirstOrDefault(t => t.Name == "All Photos"); if (allPhotosTag != null) allPhotosTag.Count = (int)count - deletedCount; } catch { } }

        private async Task SaveMetadataAsync() { try { var json = JsonSerializer.Serialize(MetadataStore); await File.WriteAllTextAsync(MetadataFilePath, json); } catch { } }
        private async Task LoadMetadataAsync() { try { if (File.Exists(MetadataFilePath)) { var json = await File.ReadAllTextAsync(MetadataFilePath); var data = JsonSerializer.Deserialize<Dictionary<string, MetadataEntry>>(json); if (data != null) { MetadataStore = data; var uniqueTags = MetadataStore.Values.SelectMany(v => v.Tags).Distinct(); foreach (var tag in uniqueTags) if (!Tags.Any(t => t.Name == tag)) Tags.Add(new TagItem { Name = tag, Count = 0 }); } } } catch { } }
        private async Task CleanupStaleMetadataAsync() { await Task.Run(() => { var keysToRemove = MetadataStore.Keys.Where(path => !File.Exists(path)).ToList(); foreach (var key in keysToRemove) MetadataStore.Remove(key); if (keysToRemove.Count > 0) _ = SaveMetadataAsync(); }); }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(storage, value)) return false;
            
            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            
            if (propertyName == "IsSelected" || propertyName == "IsSelectMode")
            {
                OnPropertyChanged(nameof(SelectedCount));
            }
            
            return true;
        }
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}