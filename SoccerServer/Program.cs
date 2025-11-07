using SoccerServer.Models;
using SoccerServer.Services;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoccerServer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // --- ⚽ サービス (DI) の設定 ⚽ ---
            builder.Services.AddControllers();

            // 1. ゲームロジックを「シングルトン」として登録
            builder.Services.AddSingleton<GameLogic>();

            // 2. WebSocket接続を管理するマネージャーを登録
            builder.Services.AddSingleton<SoccerServer.Services.WebSocketManager>(); // 曖昧な参照を避けるため完全指定

            // 3. 60FPSのゲームループをバックグラウンドサービスとして登録
            builder.Services.AddHostedService<GameLoopService>();

            // 4. JSONシリアライザーの設定 (オプション: JSONの命名規則をキャメルケースに統一)
            builder.Services.AddControllers().AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
            });

            var app = builder.Build();

            // --- ⚽ HTTP リクエストパイプラインの設定 ⚽ ---
            // app.UseHttpsRedirection(); // HTTPSリダイレクトは無効化 (ws:// のため)

            app.UseAuthorization();
            app.MapControllers(); // コントローラーを有効化

            // --- ⚽ WebSocket ミドルウェアを有効化 ⚽ ---
            var webSocketOptions = new WebSocketOptions
            {
                KeepAliveInterval = TimeSpan.FromSeconds(120)
            };
            app.UseWebSockets(webSocketOptions);

            // WebSocket 接続を処理するカスタムミドルウェア
            app.Use(async (context, next) =>
            {
                // /ws パスへのリクエストを WebSocket として処理
                if (context.Request.Path == "/ws")
                {
                    if (context.WebSockets.IsWebSocketRequest)
                    {
                        // 接続を受け入れる
                        using (WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync())
                        {
                            // 接続マネージャーとゲームロジックを取得
                            var services = app.Services.CreateScope().ServiceProvider;
                            // 曖昧な参照を避けるため完全指定
                            var webSocketManager = services.GetRequiredService<SoccerServer.Services.WebSocketManager>();

                            string socketId = Guid.NewGuid().ToString();
                            webSocketManager.AddSocket(socketId, webSocket);
                            Console.WriteLine($"[Server] WebSocket connected: {socketId}");

                            // 接続が切断されるまで待機 (切断管理)
                            await HandleWebSocketConnection(webSocket, socketId, webSocketManager);
                        }
                    }
                    else
                    {
                        context.Response.StatusCode = 400; // Bad Request
                    }
                }
                else
                {
                    await next(); // 他のパスは次のミドルウェアへ
                }
            });


            app.Run();
        }

        // WebSocket 接続の生存管理 (Program クラス内部に移動)
        // 曖昧な参照を避けるため引数で完全指定
        private static async Task HandleWebSocketConnection(WebSocket webSocket, string socketId, SoccerServer.Services.WebSocketManager manager)
        {
            var buffer = new byte[1024 * 4];

            // 接続が開いている間、ダミーの受信ループを実行
            WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            while (!result.CloseStatus.HasValue)
            {
                // TODO: クライアントからのメッセージを処理する場合はここ
                result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            }

            // 接続が閉じたらマネージャーから削除
            await manager.RemoveSocketAsync(socketId);
            Console.WriteLine($"[Server] WebSocket disconnected: {socketId}");
        }
    }
}