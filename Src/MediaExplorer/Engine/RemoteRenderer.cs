using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Windows.Data.Json;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using Windows.UI.Xaml.Media.Imaging;

namespace BrowserCore.Engine
{
    public class RemoteRenderer : IDisposable
    {
        private MessageWebSocket _socket;
        private DataWriter _writer;
        private string _serverUrl;
        private bool _connected;
        private bool _disposed;

        public event Action<BitmapImage> ScreenshotReceived;
        public event Action<string> TitleReceived;
        public event Action<string> ErrorOccurred;
        public event Action Connected;
        public event Action Disconnected;

        public bool IsConnected => _connected;
        public string ServerUrl => _serverUrl;

        public async Task<bool> ConnectAsync(string serverUrl)
        {
            try
            {
                if (_connected) Disconnect();

                _serverUrl = serverUrl?.Trim();
                if (string.IsNullOrEmpty(_serverUrl)) return false;

                if (!_serverUrl.StartsWith("ws://") && !_serverUrl.StartsWith("wss://"))
                    _serverUrl = "ws://" + _serverUrl;

                _socket = new MessageWebSocket();
                _socket.Control.MessageType = SocketMessageType.Utf8;
                _socket.MessageReceived += OnMessageReceived;
                _socket.Closed += OnSocketClosed;

                await _socket.ConnectAsync(new Uri(_serverUrl));
                _writer = new DataWriter(_socket.OutputStream);
                _connected = true;

                Connected?.Invoke();
                DevToolsLogger.Log("[DIAG:REMOTE] Connected to " + _serverUrl);
                return true;
            }
            catch (Exception ex)
            {
                DevToolsLogger.Log("[DIAG:REMOTE] Connect failed: " + ex.Message);
                ErrorOccurred?.Invoke("Connection failed: " + ex.Message);
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                _connected = false;
                _writer?.DetachStream();
                _writer?.Dispose();
                _writer = null;
                _socket?.Close(1000, "Client disconnect");
                _socket?.Dispose();
                _socket = null;
                Disconnected?.Invoke();
            }
            catch { }
        }

        public async Task RenderAsync(string url, int width = 412, int height = 915, int waitMs = 3000)
        {
            if (!_connected) return;
            await SendAsync(new
            {
                type = "render",
                url = url,
                width = width,
                height = height,
                waitMs = waitMs
            });
        }

        public async Task ClickAsync(int x, int y)
        {
            if (!_connected) return;
            await SendAsync(new { type = "click", x = x, y = y });
        }

        public async Task TypeAsync(string selector, string text)
        {
            if (!_connected) return;
            await SendAsync(new { type = "type", selector = selector, text = text });
        }

        public async Task PressKeyAsync(string key)
        {
            if (!_connected) return;
            await SendAsync(new { type = "press", key = key });
        }

        public async Task ScrollAsync(int deltaY)
        {
            if (!_connected) return;
            await SendAsync(new { type = "scroll", deltaY = deltaY });
        }

        public async Task RequestScreenshotAsync()
        {
            if (!_connected) return;
            await SendAsync(new { type = "screenshot" });
        }

        public async Task RequestTextAsync()
        {
            if (!_connected) return;
            await SendAsync(new { type = "getText" });
        }

        public async Task CloseBrowserAsync()
        {
            if (!_connected) return;
            await SendAsync(new { type = "close" });
        }

        private async Task SendAsync(object msg)
        {
            try
            {
                if (_writer == null) return;
                string json = JsonValue.Parse("{}").Stringify();
                // Simple JSON serialization
                json = NewtonsoftJson(msg);
                var buffer = System.Text.Encoding.UTF8.GetBytes(json);
                _writer.WriteBytes(buffer);
                await _writer.StoreAsync();
            }
            catch (Exception ex)
            {
                DevToolsLogger.Log("[DIAG:REMOTE] Send error: " + ex.Message);
            }
        }

        private void OnMessageReceived(MessageWebSocket sender, MessageWebSocketMessageReceivedEventArgs args)
        {
            try
            {
                using (var reader = args.GetDataReader())
                {
                    reader.UnicodeEncoding = UnicodeEncoding.Utf8;
                    string json = reader.ReadString(reader.UnconsumedBufferLength);
                    HandleMessage(json);
                }
            }
            catch (Exception ex)
            {
                DevToolsLogger.Log("[DIAG:REMOTE] Receive error: " + ex.Message);
            }
        }

        private void HandleMessage(string json)
        {
            try
            {
                var msg = JsonValue.Parse(json)?.GetObject();
                if (msg == null) return;

                string type = msg.ContainsKey("type") ? msg.GetNamedString("type") : "";

                switch (type)
                {
                    case "screenshot":
                        if (msg.ContainsKey("data"))
                        {
                            string b64 = msg.GetNamedString("data");
                            var bmp = DecodeBase64Image(b64);
                            if (bmp != null)
                                ScreenshotReceived?.Invoke(bmp);
                        }
                        break;

                    case "title":
                        if (msg.ContainsKey("text"))
                            TitleReceived?.Invoke(msg.GetNamedString("text"));
                        break;

                    case "ready":
                        DevToolsLogger.Log("[DIAG:REMOTE] Page ready: " + (msg.ContainsKey("title") ? msg.GetNamedString("title") : ""));
                        break;

                    case "error":
                        string errMsg = msg.ContainsKey("message") ? msg.GetNamedString("message") : "Unknown error";
                        DevToolsLogger.Log("[DIAG:REMOTE] Server error: " + errMsg);
                        ErrorOccurred?.Invoke(errMsg);
                        break;

                    case "hello":
                        DevToolsLogger.Log("[DIAG:REMOTE] Server: " + (msg.ContainsKey("message") ? msg.GetNamedString("message") : ""));
                        break;

                    case "text":
                        DevToolsLogger.Log("[DIAG:REMOTE] Text received: " + (msg.ContainsKey("content") ? msg.GetNamedString("content").Length : 0) + " chars");
                        break;
                }
            }
            catch (Exception ex)
            {
                DevToolsLogger.Log("[DIAG:REMOTE] Parse error: " + ex.Message);
            }
        }

        private void OnSocketClosed(IWebSocket sender, WebSocketClosedEventArgs args)
        {
            _connected = false;
            DevToolsLogger.Log("[DIAG:REMOTE] Disconnected: code=" + args.Code + " reason=" + args.Reason);
            Disconnected?.Invoke();
        }

        private static BitmapImage DecodeBase64Image(string b64)
        {
            try
            {
                var bytes = Convert.FromBase64String(b64);
                var bmp = new BitmapImage();
                var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
                var writer = new DataWriter(stream);
                writer.WriteBytes(bytes);
                writer.StoreAsync().AsTask().Wait();
                stream.Seek(0);
                bmp.SetSourceAsync(stream).AsTask().Wait();
                return bmp;
            }
            catch { return null; }
        }

        // Minimal JSON serializer (no external dependency)
        private static string NewtonsoftJson(object obj)
        {
            if (obj == null) return "null";
            var type = obj.GetType();
            if (type == typeof(string)) return "\"" + EscapeJson((string)obj) + "\"";
            if (type == typeof(int) || type == typeof(long) || type == typeof(double) || type == typeof(float) || type == typeof(bool))
                return obj.ToString().ToLowerInvariant();

            // Anonymous object serialization via runtime helper
            var dict = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var p in type.GetRuntimeProperties())
            {
                var val = p.GetValue(obj);
                dict[p.Name] = NewtonsoftJson(val);
            }
            var parts = new System.Collections.Generic.List<string>();
            foreach (var kv in dict)
                parts.Add("\"" + kv.Key + "\":" + kv.Value);
            return "{" + string.Join(",", parts) + "}";
        }

        private static string EscapeJson(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                Disconnect();
            }
        }
    }
}
