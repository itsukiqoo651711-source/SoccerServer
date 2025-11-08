using SoccerServer.Models;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoccerServer.Services
{
    public class GameLoopService : IHostedService, IDisposable
    {
        private readonly GameLogic _gameLogic;
        private readonly WebSocketManager _webSocketManager;
        private Timer _timer;
        private const int TICK_RATE = 60; // 60 FPS

        // JSONシリアライザーの設定は、ここで削除し、UpdateGameState内でSystem.Text.Jsonの標準を使用

        public GameLoopService(GameLogic gameLogic, WebSocketManager webSocketManager)
        {
            _gameLogic = gameLogic;
            _webSocketManager = webSocketManager;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("[Server] Starting game loop...");

            // 60FPS (約16.6msごと) で UpdateGameState メソッドを実行
            _timer = new Timer(UpdateGameState, null, TimeSpan.Zero, TimeSpan.FromMilliseconds(1000.0 / TICK_RATE));

            return Task.CompletedTask;
        }

        // 60FPSで実行されるメイン処理
        private async void UpdateGameState(object state)
        {
            try
            {
                // 1. ゲームロジックを更新
                _gameLogic.UpdateGame();

                // 2. 最新のゲーム状態を取得
                var gameState = _gameLogic.GetState();

                // 3. ゲーム状態をJSON文字列に変換
                // System.Text.Json の標準設定を使用 (安全性を高めるため)
                string jsonState = "";
                try
                {
                    jsonState = JsonSerializer.Serialize(gameState);
                }
                catch (Exception jsonEx)
                {
                    // JSONシリアライズ失敗時は、ログに出力して処理を中断
                    Console.WriteLine($"[Server Error] JSON Serialization Failed: {jsonEx.Message}");
                    return;
                }

                // 4. 全クライアントにブロードキャスト
                await _webSocketManager.BroadcastAsync(jsonState);
            }
            catch (Exception ex)
            {
                // ゲームロジック内で未処理の例外が発生した場合のログ
                Console.WriteLine($"[Server Error] Error in game loop broadcast: {ex.Message}");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Console.WriteLine("[Server] Stopping game loop...");
            _timer?.Change(Timeout.Infinite, 0); // タイマー停止
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }
    }
}