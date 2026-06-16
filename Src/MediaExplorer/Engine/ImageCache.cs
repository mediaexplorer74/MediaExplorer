using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace BrowserCore.Engine
{
    public class ImageCacheEntry
    {
        public string Url { get; set; }
        public BitmapImage Image { get; set; }
        public DateTime LastAccess { get; set; }
        public long SizeBytes { get; set; }
    }

    public class ImageCache
    {
        private readonly Dictionary<string, ImageCacheEntry> _cache = new Dictionary<string, ImageCacheEntry>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> _accessOrder = new List<string>();
        private readonly int _maxEntries;
        private readonly long _maxSizeBytes;
        private long _currentSizeBytes;
        private static ImageCache _instance;

        public static ImageCache Instance => _instance ?? (_instance = new ImageCache(20, 20 * 1024 * 1024));

        public int Count => _cache.Count;
        public long SizeBytes => _currentSizeBytes;

        public ImageCache(int maxEntries = 20, long maxSizeBytes = 20 * 1024 * 1024)
        {
            _maxEntries = maxEntries;
            _maxSizeBytes = maxSizeBytes;
        }

        public BitmapImage Get(string url)
        {
            if (string.IsNullOrEmpty(url)) return null;
            ImageCacheEntry entry;
            if (_cache.TryGetValue(url, out entry))
            {
                entry.LastAccess = DateTime.UtcNow;
                TouchEntry(url);
                return entry.Image;
            }
            return null;
        }

        public void Put(string url, BitmapImage image, long sizeBytes = 0)
        {
            if (string.IsNullOrEmpty(url) || image == null) return;

            if (_cache.ContainsKey(url))
            {
                var existing = _cache[url];
                _currentSizeBytes -= existing.SizeBytes;
                existing.Image = image;
                existing.SizeBytes = sizeBytes;
                existing.LastAccess = DateTime.UtcNow;
                _currentSizeBytes += sizeBytes;
                TouchEntry(url);
                EvictIfNeeded();
                return;
            }

            while (_cache.Count >= _maxEntries || _currentSizeBytes > _maxSizeBytes)
            {
                if (!EvictOldest()) break;
            }

            var entry = new ImageCacheEntry
            {
                Url = url,
                Image = image,
                LastAccess = DateTime.UtcNow,
                SizeBytes = sizeBytes
            };
            _cache[url] = entry;
            _accessOrder.Add(url);
            _currentSizeBytes += sizeBytes;
        }

        public bool Contains(string url)
        {
            return !string.IsNullOrEmpty(url) && _cache.ContainsKey(url);
        }

        public void Remove(string url)
        {
            ImageCacheEntry entry;
            if (_cache.TryGetValue(url, out entry))
            {
                _currentSizeBytes -= entry.SizeBytes;
                _cache.Remove(url);
                _accessOrder.Remove(url);
            }
        }

        public void Clear()
        {
            _cache.Clear();
            _accessOrder.Clear();
            _currentSizeBytes = 0;
            GC.Collect(0, GCCollectionMode.Optimized);
        }

        public string GetStats()
        {
            return _cache.Count + " imgs, " + (_currentSizeBytes / 1024) + "KB";
        }

        private void TouchEntry(string url)
        {
            _accessOrder.Remove(url);
            _accessOrder.Add(url);
        }

        private bool EvictOldest()
        {
            if (_accessOrder.Count == 0) return false;
            string oldest = _accessOrder[0];
            ImageCacheEntry entry;
            if (_cache.TryGetValue(oldest, out entry))
            {
                _currentSizeBytes -= entry.SizeBytes;
                _cache.Remove(oldest);
            }
            _accessOrder.RemoveAt(0);
            return true;
        }

        private void EvictIfNeeded()
        {
            int safety = 0;
            while ((_cache.Count > _maxEntries || _currentSizeBytes > _maxSizeBytes) && safety < 10)
            {
                if (!EvictOldest()) break;
                safety++;
            }
        }

        public static async Task<BitmapImage> LoadCachedAsync(string url, int decodeWidth = 0)
        {
            if (string.IsNullOrEmpty(url)) return null;

            var cache = Instance;
            var cached = cache.Get(url);
            if (cached != null) return cached;

            try
            {
                var http = new System.Net.Http.HttpClient();
                http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                var resp = await http.GetAsync(new Uri(url));
                if (!resp.IsSuccessStatusCode) return null;
                var stream = await resp.Content.ReadAsStreamAsync();

                long sizeBytes = resp.Content.Headers.ContentLength ?? 0;
                var memStream = new MemoryStream();
                await stream.CopyToAsync(memStream);
                memStream.Position = 0;

                var bmp = new BitmapImage();
                if (decodeWidth > 0)
                    bmp.DecodePixelWidth = decodeWidth;

                var ras = memStream.AsRandomAccessStream();
                await bmp.SetSourceAsync(ras);

                cache.Put(url, bmp, sizeBytes > 0 ? sizeBytes : memStream.Length);
                return bmp;
            }
            catch
            {
                return null;
            }
        }
    }
}
