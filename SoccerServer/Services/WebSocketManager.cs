using System.Net.WebSockets;
using System.Collections.Concurrent;
using System.Text;

namespace SoccerServer.Services
{
    // 接続されている全クライアントを管理する
    public class WebSocketManager
    {
        // スレッドセーフなDictionary
        private ConcurrentDictionary<string, WebSocket> _sockets = new ConcurrentDictionary<string, WebSocket>();

        public void AddSocket(string id, WebSocket socket)
        {
            _sockets.TryAdd(id, socket);
        }

        public async Task RemoveSocketAsync(string id)
        {
            if (_sockets.TryRemove(id, out WebSocket socket))
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Connection closed", CancellationToken.None);
                }
            }
        }

        // 全クライアントにJSONデータをブロードキャストする
        public async Task BroadcastAsync(string message)
        {
            var buffer = Encoding.UTF8.GetBytes(message);
            var segment = new ArraySegment<byte>(buffer);

            // 全ソケットに対して並列で送信
            var tasks = _sockets.Values.Select(async (socket) =>
            {
                if (socket.State == WebSocketState.Open)
                {
                    try
                    {
                        await socket.SendAsync(segment, WebSocketMessageType.Text, true, CancellationToken.None);
                    }
                    catch (WebSocketException)
                    {
                        // 送信失敗（クライアントが突然切断など）
                        // このソケットを削除する処理を呼び出す (IDが不明なため、ここでは省略)
                    }
                }
            });

            await Task.WhenAll(tasks);
        }
    }
}