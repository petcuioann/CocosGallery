using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Collections.Generic;
using Windows.Storage;
using Windows.Storage.Search;
using Windows.Storage.Streams;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CocosGallery
{
    public class IncrementalMediaSource : ObservableCollection<MediaItem>, ISupportIncrementalLoading
    {
        private StorageFileQueryResult? _queryResult;
        private uint _loadedCount = 0;
        private readonly string? _folderPath;
        private readonly string? _tagFilter;
        private readonly string? _searchQuery;
        private readonly HashSet<string> _activeExtensions;

        public IncrementalMediaSource(string? folderPath = null, string? tagFilter = null, string? searchQuery = null, HashSet<string>? activeExtensions = null)
        {
            _folderPath = folderPath;
            _tagFilter = tagFilter;
            _searchQuery = searchQuery;
            _activeExtensions = activeExtensions ?? new HashSet<string>();
        }

        public bool HasMoreItems => true;

        // pre: count is a positive integer
        // post: more items are loaded into the collection
        // except: returns empty result on null dispatcher
        // complexity: O(count) in worst case
        public Windows.Foundation.IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)
        {
            return AsyncInfo.Run(async (cancellationToken) =>
            {
                if (_queryResult == null)
                {
                    var options = new QueryOptions(CommonFileQuery.OrderByDate, new[] { ".jpg", ".png", ".jpeg", ".dng", ".mp4", ".mov", ".mkv" })
                    {
                        FolderDepth = FolderDepth.Deep
                    };

                    if (!string.IsNullOrEmpty(_searchQuery)) options.UserSearchFilter = _searchQuery;

                    StorageFolder folder = KnownFolders.PicturesLibrary;
                    try
                    {
                        if (!string.IsNullOrEmpty(_folderPath)) folder = await StorageFolder.GetFolderFromPathAsync(_folderPath);
                    }
                    catch { }

                    _queryResult = folder.CreateFileQueryWithOptions(options);
                }

                uint batchSize = 50;
                var files = await _queryResult.GetFilesAsync(_loadedCount, batchSize);

                uint added = 0;
                uint scanned = 0;

                if (files.Count == 0) return new LoadMoreItemsResult { Count = 0 };

                var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
                if (dispatcher == null) return new LoadMoreItemsResult { Count = 0 };

                var processedData = new List<(StorageFile File, IRandomAccessStream? Stream, MetadataEntry? Entry, bool IsVideo)>();

                foreach (var file in files)
                {
                    if (MainViewModel.IsViewerOpen) break;

                    scanned++;

                    string fileExt = file.FileType.ToLower();
                    if (_activeExtensions.Count > 0 && !_activeExtensions.Contains(fileExt)) continue;

                    bool isVideo = fileExt == ".mp4" || fileExt == ".mov" || fileExt == ".mkv";
                    var entry = MainViewModel.MetadataStore.ContainsKey(file.Path) ? MainViewModel.MetadataStore[file.Path] : null;

                    bool isDeleted = entry?.IsDeleted ?? false;
                    if (isDeleted && _tagFilter != "Recycle Bin") continue;
                    if (!isDeleted && _tagFilter == "Recycle Bin") continue;
                    if (_tagFilter == "Favorites" && (entry == null || !entry.IsFavorite)) continue;

                    if (_tagFilter != null && _tagFilter != "All Photos" && _tagFilter != "Favorites" && _tagFilter != "Recycle Bin")
                    {
                        if (entry == null || !entry.Tags.Contains(_tagFilter)) continue;
                    }

                    IRandomAccessStream? thumbStream = null;
                    var existingItem = MainViewModel.GlobalSelectedItems.FirstOrDefault(x => x.FilePath == file.Path);

                    if (existingItem == null)
                    {
                        try
                        {
                            thumbStream = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.ListView, 200);
                        }
                        catch { }
                    }

                    processedData.Add((file, thumbStream, entry, isVideo));
                }

                var tcs = new TaskCompletionSource<bool>();

                dispatcher.TryEnqueue(async () =>
                {
                    foreach (var data in processedData)
                    {
                        var existingItem = MainViewModel.GlobalSelectedItems.FirstOrDefault(x => x.FilePath == data.File.Path);
                        MediaItem item;

                        if (existingItem != null)
                        {
                            item = existingItem;
                        }
                        else
                        {
                            BitmapImage bmp = new BitmapImage();
                            if (data.Stream != null)
                            {
                                await bmp.SetSourceAsync(data.Stream);
                                data.Stream.Dispose();
                            }
                            item = new MediaItem(data.File.Name, bmp, data.IsVideo, data.File.Path, data.File);
                        }

                        item.IsSelectModeActive = MainViewModel.GlobalIsSelectMode;

                        if (data.Entry != null)
                        {
                            item.IsFavorite = data.Entry.IsFavorite;
                            item.IsDeleted = data.Entry.IsDeleted;
                            item.Tags.Clear();
                            foreach (var t in data.Entry.Tags) item.Tags.Add(t);
                        }

                        this.Add(item);
                        added++;
                    }
                    tcs.SetResult(true);
                });

                await tcs.Task;

                _loadedCount += scanned;
                return new LoadMoreItemsResult { Count = added };
            });
        }
    }
}