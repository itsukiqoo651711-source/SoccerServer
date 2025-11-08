using System.Text.Json.Serialization;
using System.Collections.Generic; // Dictionary を使用するため

namespace SoccerServer.Models
{
    // server.js の gameState.ball に対応
    public class BallData
    {
        [JsonPropertyName("x")]
        public float X { get; set; } = 400;
        [JsonPropertyName("y")]
        public float Y { get; set; } = 300;
        [JsonPropertyName("vx")]
        public float VX { get; set; } = 0;
        [JsonPropertyName("vy")]
        public float VY { get; set; } = 0;
        [JsonPropertyName("z")]
        public float Z { get; set; } = 0;
        [JsonPropertyName("vz")]
        public float VZ { get; set; } = 0;
    }

    // server.js の player.stats に対応
    public class PlayerStats
    {
        [JsonPropertyName("speed")]
        public float Speed { get; set; }
        [JsonPropertyName("shot")]
        public float Shot { get; set; }
        [JsonPropertyName("pass")]
        public float Pass { get; set; } = 80;
        [JsonPropertyName("dribble")]
        public float Dribble { get; set; }
        [JsonPropertyName("tackle")]
        public float Tackle { get; set; }
        [JsonPropertyName("shotRangeMult")]
        public float ShotRangeMult { get; set; } = 1;

        [JsonPropertyName("shotMult")]
        public float ShotMult { get; set; } = 1;
    }

    // server.js の player.ranks に対応
    public class PlayerRanks
    {
        [JsonPropertyName("spd")]
        public string Spd { get; set; }
        [JsonPropertyName("sht")]
        public string Sht { get; set; }
        [JsonPropertyName("pas")]
        public string Pas { get; set; }
        [JsonPropertyName("drb")]
        public string Drb { get; set; }
        [JsonPropertyName("tck")]
        public string Tck { get; set; }
    }

    // server.js の gameState.players[id] に対応
    public class PlayerData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; }
        [JsonPropertyName("x")]
        public float X { get; set; }
        [JsonPropertyName("y")]
        public float Y { get; set; }
        [JsonPropertyName("vx")]
        public float VX { get; set; } = 0;
        [JsonPropertyName("vy")]
        public float VY { get; set; } = 0;
        [JsonPropertyName("team")]
        public string Team { get; set; }
        [JsonPropertyName("role")]
        public string Role { get; set; }
        [JsonPropertyName("imageKey")]
        public string ImageKey { get; set; }
        [JsonPropertyName("isBallHolder")]
        public bool IsBallHolder { get; set; } = false;
        [JsonPropertyName("targetX")]
        public float TargetX { get; set; }
        [JsonPropertyName("targetY")]
        public float TargetY { get; set; }
        [JsonPropertyName("stats")]
        public PlayerStats Stats { get; set; }
        [JsonPropertyName("ranks")]
        public PlayerRanks Ranks { get; set; }

        // Unityクライアントが z と vz を期待しているため追加
        [JsonPropertyName("z")]
        public float Z { get; set; } = 0;
        [JsonPropertyName("vz")]
        public float VZ { get; set; } = 0;
    }

    // server.js の gameState.score に対応
    public class ScoreData
    {
        [JsonPropertyName("home")]
        public int Home { get; set; } = 0;
        [JsonPropertyName("away")]
        public int Away { get; set; } = 0;
    }

    // server.js の gameState.scorers に対応
    public class ScorerData
    {
        [JsonPropertyName("playerId")]
        public string PlayerId { get; set; }
        [JsonPropertyName("time")]
        public int Time { get; set; }
    }

    // server.js の gameState (全体) に対応
    public class GameState
    {
        [JsonPropertyName("players")]
        public Dictionary<string, PlayerData> Players { get; set; } = new Dictionary<string, PlayerData>();

        [JsonPropertyName("ball")]
        public BallData Ball { get; set; } = new BallData();
        [JsonPropertyName("score")]
        public ScoreData Score { get; set; } = new ScoreData();
        [JsonPropertyName("time")]
        public int Time { get; set; } = 3 * 60;
        [JsonPropertyName("matchEnded")]
        public bool MatchEnded { get; set; } = false;
        [JsonPropertyName("scorers")]
        public List<ScorerData> Scorers { get; set; } = new List<ScorerData>();

        // --- ★★★ ここから追加 ★★★ ---

        [JsonPropertyName("matchStatus")]
        public string MatchStatus { get; set; } = "WaitingToStart"; // "WaitingToStart", "Playing", "GoalScored", "MatchEnd"

        [JsonPropertyName("goalMessage")]
        public string GoalMessage { get; set; } = ""; // "GOAL!" などのメッセージ用

        // --- ★★★ 追加ここまで ★★★ ---
    }
}