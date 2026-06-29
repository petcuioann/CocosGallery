using System.Collections.Concurrent;
using Microsoft.UI.Xaml.Media.Imaging;

namespace CocosGallery {
    public static class ThumbnailCacheManager {
        private static readonly ConcurrentDictionary<ulong, ConcurrentDictionary<string, BitmapImage>> _instanceCaches = new();

        public static void CacheThumbnail(ulong windowId, string filePath, BitmapImage image) =>
            _instanceCaches.GetOrAdd(windowId, _ => new ConcurrentDictionary<string, BitmapImage>())[filePath] = image;

        public static BitmapImage? GetThumbnail(ulong windowId, string filePath) =>
            _instanceCaches.TryGetValue(windowId, out var cache) && cache.TryGetValue(filePath, out var image) ? image : null;

        public static void RemoveThumbnail(ulong windowId, string filePath) {
            if (_instanceCaches.TryGetValue(windowId, out var cache))
                cache.TryRemove(filePath, out _);
        }

        public static void ClearInstance(ulong windowId) {
            if (_instanceCaches.TryRemove(windowId, out var cache))
                cache.Clear();
        }
    }
}
