using SoccerServer.Models;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System;

namespace SoccerServer.Services
{
    public class GameLogic
    {
        // 状態
        private GameState _gameState = new GameState();
        private bool _isPaused = false;
        private string _closestPlayerToBall = null;
        private string _kickOffTeam = "home";
        private string _lastScorer = null;
        private string _currentHolderId = null;
        private readonly Random _random = new Random();

        // ★★★ ゲームフロー管理用タイマー ★★★
        private int _pauseTimer = 0; // "Goal!" や "MatchEnd" の表示用タイマー
        private int _frameCounter = 0; // ★★★ 1秒カウント用のフレームカウンター ★★★
        private const int GOAL_PAUSE_FRAMES = 180; // ゴール後の停止時間 (3秒 * 60fps)
        // ★★★ ここまで ★★★

        // --- 定数定義 (AIバランス調整済み) ---
        private const float FIELD_WIDTH = 800f;
        private const float FIELD_HEIGHT = 600f;
        private const int PLAYER_COUNT = 22; // 11 v 11
        private const float GLOBAL_SPEED_FACTOR = 0.5f;
        private const float PLAYER_SPEED = 2.0f * GLOBAL_SPEED_FACTOR;
        private const float BALL_DRAG = 0.98f;
        private const float CENTER_Y = FIELD_HEIGHT / 2f;
        private const float SIDE_Y_L = FIELD_HEIGHT * 0.25f;
        private const float SIDE_Y_R = FIELD_HEIGHT * 0.75f;
        private const float GOAL_POST_Y_TOP = FIELD_HEIGHT * 0.35f;
        private const float GOAL_POST_Y_BOTTOM = FIELD_HEIGHT * 0.65f;
        private const float GOAL_LINE_X_HOME = 30f;
        private const float GOAL_LINE_X_AWAY = FIELD_WIDTH - 30f;
        private const float GOAL_HEIGHT = 50f;
        private const float PLAYER_KICK_RANGE = 10f;
        private const float BALL_SPEED_FACTOR = 0.70f;
        private const float PLAYER_SHOT_RANGE_DEFAULT = 150f;
        private const float AI_PASS_RANGE = 250f;
        private const float AI_FREE_SPACE_DISTANCE = 70f;
        private const float AI_PASS_ROUTE_CLEARANCE = 20f;
        private const float AI_PASS_SCORE_THRESHOLD = 80f;
        private const float AI_PASS_SCORE_GREAT = 350f;
        private const float AI_DRIBBLE_THRESHOLD = 130f; // ドリブラーかどうかの閾値
        private const float PLAYER_KICK_HEIGHT = 10f;
        private const float GK_CATCH_HEIGHT = 20f;
        // ★ ドリブル判断用の定数
        private const float AI_DRIBBLE_SAFE_DISTANCE = 120f; // これ以上離れていれば安全
        private const float AI_DRIBBLER_RISK_DISTANCE = 40f;  // これより近いとリスク

        private readonly Dictionary<string, float[]> HOME_POSITIONS;
        private readonly Dictionary<string, float[]> AWAY_POSITIONS;

        public GameLogic()
        {
            // ★ Home: 4-2-3-1
            HOME_POSITIONS = new Dictionary<string, float[]>
            {
                { "player0", new[] { 60f, CENTER_Y } }, // GK
                { "player1", new[] { 150f, 100f } }, // LB
                { "player2", new[] { 130f, 250f } }, // CB
                { "player3", new[] { 130f, 350f } }, // CB
                { "player4", new[] { 150f, 500f } }, // RB
                { "player5", new[] { 280f, 200f } }, // DMF
                { "player6", new[] { 280f, 400f } }, // DMF
                { "player7", new[] { 400f, 150f } }, // LMF
                { "player8", new[] { 400f, CENTER_Y } }, // AMF
                { "player9", new[] { 400f, 450f } }, // RMF
                { "player10", new[] { 500f, CENTER_Y } } // FW
            };

            // ★ Away: 4-3-3 (カウンター戦術)
            AWAY_POSITIONS = new Dictionary<string, float[]>
            {
                { "player11", new[] { FIELD_WIDTH - 60f, CENTER_Y } }, // GK
                // DF (4)
                { "player12", new[] { FIELD_WIDTH - 150f, 100f } }, // LB
                { "player13", new[] { FIELD_WIDTH - 130f, 250f } }, // CB
                { "player14", new[] { FIELD_WIDTH - 130f, 350f } }, // CB
                { "player15", new[] { FIELD_WIDTH - 150f, 500f } }, // RB
                // MF (3)
                { "player16", new[] { FIELD_WIDTH - 250f, CENTER_Y } }, // DMF (守備的MF)
                { "player17", new[] { FIELD_WIDTH - 320f, 200f } }, // CMF (中央MF)
                { "player18", new[] { FIELD_WIDTH - 320f, 400f } }, // CMF (中央MF)
                // FW (3)
                { "player19", new[] { FIELD_WIDTH - 450f, 150f } }, // LW (ウィング)
                { "player20", new[] { FIELD_WIDTH - 480f, CENTER_Y } }, // CF
                { "player21", new[] { FIELD_WIDTH - 450f, 450f } }  // RW (ウィング)
            };

            InitializePlayers();
            _gameState.MatchStatus = "WaitingToStart";
            _gameState.Time = 3 * 60; // 3分
            ResetBallAndPlayers(true); // 選手を配置
        }

        public GameState GetState() => _gameState;

        // --- ★★★ ゲームフロー制御メソッド (ここから) ★★★ ---

        public void StartGame()
        {
            if (_gameState.MatchStatus == "WaitingToStart")
            {
                _gameState.MatchStatus = "Playing";
                _kickOffTeam = "home";
                _frameCounter = 0;
                ResetForKickoff(true);
            }
        }

        public void ContinueGame()
        {
            if (_gameState.MatchStatus == "MatchEnd")
            {
                _gameState.Score.Home = 0;
                _gameState.Score.Away = 0;
                _gameState.Time = 3 * 60;
                _gameState.Scorers.Clear();
                _gameState.MatchEnded = false;
                _gameState.MatchStatus = "Playing";
                _kickOffTeam = "home";
                _frameCounter = 0;
                ResetForKickoff(true);
            }
        }

        // --- ★★★ (ここまで) ★★★ ---


        private float Hypot(float a, float b) => (float)Math.Sqrt(a * a + b * b);

        private string ToRank(float value)
        {
            if (value >= 200) return "S";
            if (value >= 150) return "A";
            if (value >= 120) return "B";
            if (value >= 100) return "C";
            if (value >= 80) return "D";
            return "E";
        }

        private void InitializePlayers()
        {
            for (int i = 0; i < PLAYER_COUNT; i++)
            {
                string playerId = $"player{i}";
                bool isHome = i < 11;
                string imageKey = isHome ? "player_home" : "player_away";

                string role = "FW";
                float speedMult = 1f;
                float dribbleMult = 1f;
                float tackleMult = 1f;
                float shotRangeMult = 1f;
                float shotMult = 1f;
                float passMult = 1f;
                string displayName = playerId;

                // ★ Home (4-2-3-1) と Away (4-3-3) で役割を変更
                switch (i)
                {
                    // --- Home (0-10) 4-2-3-1 ---
                    case 0: role = "GK"; imageKey = "keeper_home"; break;
                    case 1: role = "DF"; break; // LB
                    case 2: role = "DF"; break; // CB
                    case 3: role = "DF"; break; // CB
                    case 4: role = "DF"; break; // RB
                    case 5: role = "MF"; break; // DMF
                    case 6: role = "MF"; break; // DMF
                    case 7: role = "MF"; break; // LMF
                    case 8: role = "MF"; break; // AMF
                    case 9: role = "MF"; break; // RMF
                    case 10: role = "FW"; break; // FW

                    // --- Away (11-21) 4-3-3 ---
                    case 11: role = "GK"; imageKey = "keeper_away"; break;
                    case 12: role = "DF"; break; // LB
                    case 13: role = "DF"; break; // CB
                    case 14: role = "DF"; break; // CB
                    case 15: role = "DF"; break; // RB
                    case 16: role = "MF-D"; break; // DMF (守備的MF)
                    case 17: role = "MF-C"; break; // CMF (中央MF)
                    case 18: role = "MF-C"; break; // CMF (中央MF)
                    case 19: role = "FW-W"; break; // LW (ウィング)
                    case 20: role = "FW-C"; break; // CF
                    case 21: role = "FW-W"; break; // RW (ウィング)
                }

                float baseSpeed = 70 + (float)(_random.NextDouble() * 30);
                float baseShot = 30 + (float)(_random.NextDouble() * 50);
                float baseDribble = 100 + (float)(_random.NextDouble() * 30);
                float baseTackle = 70 + (float)(_random.NextDouble() * 30);
                float finalSpeed = baseSpeed * speedMult;
                float finalShot = baseShot * shotMult;
                float finalDribble = baseDribble * dribbleMult;
                float finalTackle = baseTackle * tackleMult;
                float finalPass = 80 * passMult;

                var positions = isHome ? HOME_POSITIONS : AWAY_POSITIONS;
                float xPos = (float)(_random.NextDouble() * FIELD_WIDTH);
                float yPos = (float)(_random.NextDouble() * FIELD_HEIGHT);
                if (positions.TryGetValue(playerId, out float[] pos))
                {
                    xPos = pos[0];
                    yPos = pos[1];
                }

                _gameState.Players[playerId] = new PlayerData
                {
                    Id = playerId,
                    DisplayName = displayName.StartsWith("player") ? playerId : displayName,
                    X = xPos,
                    Y = yPos,
                    VX = 0,
                    VY = 0,
                    Team = isHome ? "home" : "away",
                    Role = role,
                    ImageKey = imageKey,
                    IsBallHolder = false,
                    TargetX = xPos,
                    TargetY = yPos,
                    Stats = new PlayerStats
                    {
                        Speed = finalSpeed,
                        Shot = finalShot,
                        Pass = finalPass,
                        Dribble = finalDribble,
                        Tackle = finalTackle,
                        ShotRangeMult = shotRangeMult,
                        ShotMult = shotMult
                    },
                    Ranks = new PlayerRanks
                    {
                        Spd = ToRank(finalSpeed),
                        Sht = ToRank(finalShot),
                        Pas = ToRank(finalPass),
                        Drb = ToRank(finalDribble),
                        Tck = ToRank(finalTackle)
                    }
                };
            }
        }

        private void ResetBallAndPlayers(bool isInitialStart = false)
        {
            foreach (var pair in _gameState.Players)
            {
                var player = pair.Value;
                var positions = player.Team == "home" ? HOME_POSITIONS : AWAY_POSITIONS;

                if (positions.TryGetValue(player.Id, out float[] pos))
                {
                    player.X = pos[0]; player.Y = pos[1];
                }
                player.VX = 0; player.VY = 0; player.IsBallHolder = false; player.TargetX = player.X; player.TargetY = player.Y;
            }
            _currentHolderId = null;
            _gameState.Ball = new BallData { X = FIELD_WIDTH / 2f, Y = FIELD_HEIGHT / 2f, Z = 0, VZ = 0 };
        }

        private void ResetForKickoff(bool executeKickOff = false)
        {
            ResetBallAndPlayers();
            _gameState.GoalMessage = "";

            if (executeKickOff)
            {
                Debug.WriteLine($"[Server] Executing kick off. Team: {_kickOffTeam}");
                if (_kickOffTeam == "home") { _gameState.Ball.VX = 10 * GLOBAL_SPEED_FACTOR; }
                else { _gameState.Ball.VX = -10 * GLOBAL_SPEED_FACTOR; }
                _gameState.Ball.VY = (float)(_random.NextDouble() - 0.5) * 24 * GLOBAL_SPEED_FACTOR;
            }
        }

        public void UpdatePhysics()
        {
            var ball = _gameState.Ball;
            const float GRAVITY = -0.45f;
            const float GROUND_BOUNCE = -0.3f;

            ball.Z += ball.VZ;
            if (ball.Z > 0)
            {
                ball.VZ += GRAVITY;
            }
            else
            {
                ball.Z = 0;
                ball.VZ = (ball.VZ < -1) ? ball.VZ * GROUND_BOUNCE : 0;
            }

            ball.X += ball.VX;
            ball.Y += ball.VY;
            ball.VX *= BALL_DRAG;
            ball.VY *= BALL_DRAG;

            if (ball.Y < 0) { ball.Y = 0; ball.VY *= -1; }
            if (ball.Y > FIELD_HEIGHT) { ball.Y = FIELD_HEIGHT; ball.VY *= -1; }

            // --- ゴール判定 ---
            bool isGoalHome = ball.X > GOAL_LINE_X_AWAY && ball.Y > GOAL_POST_Y_TOP && ball.Y < GOAL_POST_Y_BOTTOM && ball.Z < GOAL_HEIGHT;
            bool isGoalAway = ball.X < GOAL_LINE_X_HOME && ball.Y > GOAL_POST_Y_TOP && ball.Y < GOAL_POST_Y_BOTTOM && ball.Z < GOAL_HEIGHT;

            if (isGoalHome || isGoalAway)
            {
                if (isGoalHome) { _gameState.Score.Home++; _kickOffTeam = "away"; }
                else { _gameState.Score.Away++; _kickOffTeam = "home"; }

                if (_lastScorer != null)
                {
                    _gameState.Scorers.Add(new ScorerData { PlayerId = _lastScorer, Time = _gameState.Time });
                }

                _gameState.MatchStatus = "GoalScored";
                _gameState.GoalMessage = "GOAL!";
                _pauseTimer = GOAL_PAUSE_FRAMES;

                return;
            }

            if (ball.X < 0) { ball.X = 0; ball.VX *= -1; }
            if (ball.X > FIELD_WIDTH) { ball.X = FIELD_WIDTH; ball.VX *= -1; }

            // プレイヤーの移動
            foreach (var player in _gameState.Players.Values)
            {
                float angle = (float)Math.Atan2(player.TargetY - player.Y, player.TargetX - player.X);
                float playerSpeed = (player.Stats.Speed / 100) * PLAYER_SPEED;
                float dist = Hypot(player.TargetX - player.X, player.TargetY - player.Y);

                if (dist > playerSpeed)
                {
                    player.VX = (float)Math.Cos(angle) * playerSpeed;
                    player.VY = (float)Math.Sin(angle) * playerSpeed;
                    player.X += player.VX;
                    player.Y += player.VY;
                }
                else
                {
                    player.X = player.TargetX;
                    player.Y = player.TargetY;
                    player.VX = 0;
                    player.VY = 0;
                }
            }

            // ドリブル処理
            if (_currentHolderId != null && _gameState.Players.TryGetValue(_currentHolderId, out var holder) && holder.IsBallHolder)
            {
                float playerSpeed = Hypot(holder.VX, holder.VY);
                if (playerSpeed > 0.1f)
                {
                    float angle = (float)Math.Atan2(holder.VY, holder.VX);
                    ball.X = holder.X + (float)Math.Cos(angle) * 10f;
                    ball.Y = holder.Y + (float)Math.Sin(angle) * 10f;
                    ball.VX = holder.VX * BALL_SPEED_FACTOR * (holder.Stats.Dribble / 100f);
                    ball.VY = holder.VY * BALL_SPEED_FACTOR * (holder.Stats.Dribble / 100f);
                    ball.Z = 0;
                    ball.VZ = 0;
                }
                else
                {
                    ball.X = holder.X + 5f;
                    ball.Y = holder.Y;
                    ball.VX *= BALL_DRAG;
                    ball.VY *= BALL_DRAG;
                    ball.Z = 0;
                    ball.VZ = 0;
                }
            }
        }

        // --- AIロジック (ここから) ---

        private bool IsPlayerFree(PlayerData player)
        {
            foreach (var opponent in _gameState.Players.Values)
            {
                if (opponent.Team != player.Team)
                {
                    float dist = Hypot(player.X - opponent.X, player.Y - opponent.Y);
                    if (dist < AI_FREE_SPACE_DISTANCE)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private PlayerData FindNearestOpponent(PlayerData player)
        {
            float closestDist = float.MaxValue;
            PlayerData nearestOpponent = null;
            foreach (var opponent in _gameState.Players.Values)
            {
                if (opponent.Team != player.Team)
                {
                    float dist = Hypot(player.X - opponent.X, player.Y - opponent.Y);
                    if (dist < closestDist)
                    {
                        closestDist = dist;
                        nearestOpponent = opponent;
                    }
                }
            }
            return nearestOpponent;
        }

        private bool IsPassRouteClear(PlayerData passer, PlayerData targetPlayer)
        {
            float pX = passer.X, pY = passer.Y;
            float tX = targetPlayer.X, tY = targetPlayer.Y;
            float lineLengthSq = (float)(Math.Pow(tX - pX, 2) + Math.Pow(tY - pY, 2));
            if (lineLengthSq < (AI_PASS_ROUTE_CLEARANCE * AI_PASS_ROUTE_CLEARANCE)) return true;

            foreach (var opponent in _gameState.Players.Values)
            {
                if (opponent.Team == passer.Team) continue;
                float oX = opponent.X, oY = opponent.Y;
                if (oX < Math.Min(pX, tX) - AI_PASS_ROUTE_CLEARANCE || oX > Math.Max(pX, tX) + AI_PASS_ROUTE_CLEARANCE ||
                    oY < Math.Min(pY, tY) - AI_PASS_ROUTE_CLEARANCE || oY > Math.Max(pY, tY) + AI_PASS_ROUTE_CLEARANCE)
                {
                    continue;
                }
                float t = ((oX - pX) * (tX - pX) + (oY - pY) * (tY - pY)) / lineLengthSq;
                float closestX, closestY;
                if (t < 0) { closestX = pX; closestY = pY; }
                else if (t > 1) { closestX = tX; closestY = tY; }
                else { closestX = pX + t * (tX - pX); closestY = pY + t * (tY - pY); }

                float distToLine = Hypot(oX - closestX, oY - closestY);
                if (distToLine < AI_PASS_ROUTE_CLEARANCE) return false;
            }
            return true;
        }

        // ★ AI: パス判断 (Away 4-3-3 カウンター戦術)
        private (PlayerData target, float score) FindBestPassTarget(PlayerData passer, string[] rolesToFind)
        {
            float bestScore = -float.MaxValue;
            PlayerData targetPlayer = null;
            string myTeam = passer.Team;
            float enemyGoalX = (myTeam == "home") ? FIELD_WIDTH : 0;
            float enemyGoalY = CENTER_Y;

            // GK Logic
            if (passer.Role == "GK")
            {
                // (GKロジックは変更なし)
                float closestDist = float.MaxValue;
                PlayerData bestLobTarget = null;
                float bestLobScore = -float.MaxValue;

                foreach (var teammate in _gameState.Players.Values)
                {
                    if (teammate.Team == myTeam && teammate.Id != passer.Id && rolesToFind.Any(r => teammate.Role.StartsWith(r)))
                    {
                        float dist = Hypot(passer.X - teammate.X, passer.Y - teammate.Y);
                        if (dist < AI_PASS_RANGE && IsPlayerFree(teammate))
                        {
                            if (teammate.Role.StartsWith("DF") || teammate.Role.StartsWith("MF"))
                            {
                                if (dist < closestDist && IsPassRouteClear(passer, teammate))
                                {
                                    closestDist = dist;
                                    targetPlayer = teammate; // Ground pass
                                }
                            }
                            else if (teammate.Role.StartsWith("FW") || teammate.Role.StartsWith("MF"))
                            {
                                float distToGoal = Hypot(teammate.X - enemyGoalX, teammate.Y - enemyGoalY);
                                float score = (FIELD_WIDTH - distToGoal);
                                if (score > bestLobScore)
                                {
                                    bestLobScore = score;
                                    bestLobTarget = teammate;
                                }
                            }
                        }
                    }
                }
                var bestGkTarget = targetPlayer ?? bestLobTarget;
                return (bestGkTarget, bestGkTarget != null ? 200 : -float.MaxValue);
            }

            // Field Player Logic
            foreach (var teammate in _gameState.Players.Values)
            {
                if (teammate.Team == myTeam && teammate.Id != passer.Id && rolesToFind.Any(r => teammate.Role.StartsWith(r)))
                {
                    float distToPasser = Hypot(passer.X - teammate.X, passer.Y - teammate.Y);
                    if (distToPasser < AI_PASS_RANGE)
                    {
                        float freeBonus = IsPlayerFree(teammate) ? 100 : 0;
                        float distToGoal = Hypot(teammate.X - enemyGoalX, teammate.Y - enemyGoalY);
                        float goalBonus = (FIELD_WIDTH - distToGoal);
                        float routeClearBonus = IsPassRouteClear(passer, teammate) ? 50 : -200;

                        // --- パス評価ボーナス ---
                        float forwardBonus = 0;
                        float diversityBonus = 0;
                        float xProgress = (myTeam == "home") ? (teammate.X - passer.X) : (passer.X - teammate.X);
                        float yProgress = Math.Abs(teammate.Y - passer.Y);

                        if (xProgress > 10f) { forwardBonus = Math.Min(xProgress * 1.0f, 100f); }
                        else if (xProgress < -10f) { diversityBonus = 20f; }
                        if (yProgress > 50f) { diversityBonus += Math.Min(yProgress * 0.5f, 70f); }
                        float randomNoise = (float)(_random.NextDouble() - 0.5) * 50f;

                        // ★★★ 戦術ボーナス (Away 4-3-3) ★★★
                        float tacticBonus = 0;
                        if (myTeam == "away") // Away 4-3-3 カウンター
                        {
                            // 1. ロングボール（縦に100px以上進む）を高く評価
                            if (xProgress > 100f)
                            {
                                tacticBonus += 150f; // ★ロングボールボーナス
                            }
                            // 2. ウィング(LW/RW) へのパスを高く評価 (ID変更)
                            if (teammate.Id == "player19" || teammate.Id == "player21")
                            {
                                tacticBonus += 100f; // ★サイド攻撃ボーナス
                            }
                        }
                        // ★★★ 戦術の変更ここまで ★★★

                        // 新しい評価式
                        float passScore = freeBonus + goalBonus + routeClearBonus + forwardBonus + diversityBonus + randomNoise + tacticBonus;

                        if (routeClearBonus < 0 && (passer.Role.StartsWith("DF") || passer.Role.StartsWith("MF")) && teammate.Role.StartsWith("FW"))
                        {
                            float distBonus = (distToPasser / AI_PASS_RANGE) * 50;
                            passScore = freeBonus + goalBonus + distBonus + forwardBonus + diversityBonus + randomNoise + tacticBonus;
                        }

                        if (passScore > bestScore)
                        {
                            bestScore = passScore;
                            targetPlayer = teammate;
                        }
                    }
                }
            }
            return (targetPlayer, bestScore);
        }

        // ★ ドリブル期待値 評価関数 (CS0266 エラー修正済み)
        private (float targetX, float targetY, float score) EvaluateDribble(PlayerData player, PlayerData nearestOpponent)
        {
            string myTeam = player.Team;
            float enemyGoalX = (myTeam == "home") ? FIELD_WIDTH : 0;
            float enemyGoalY = CENTER_Y;

            // 1. 基本スコア: ゴールへの近さ
            float goalBonus = (FIELD_WIDTH - Hypot(player.X - enemyGoalX, player.Y - enemyGoalY));

            float riskBonus = 0;
            float distToOpponent = float.MaxValue;
            bool isOpponentInFront = false;

            if (nearestOpponent != null)
            {
                distToOpponent = Hypot(player.X - nearestOpponent.X, player.Y - nearestOpponent.Y);
                isOpponentInFront = (myTeam == "home") ? (nearestOpponent.X > player.X) : (nearestOpponent.X < player.X);
            }

            // 2. リスク/リワード
            if (distToOpponent > AI_DRIBBLE_SAFE_DISTANCE || !isOpponentInFront)
            {
                // スペースが空いている (DFがいない)
                riskBonus = 150f; // ドリブルを強く推奨
            }
            else if (distToOpponent < AI_DRIBBLER_RISK_DISTANCE && isOpponentInFront)
            {
                // 近すぎる (危険)
                riskBonus = -150f; // パスを強く推奨
            }
            else if (isOpponentInFront)
            {
                // ここが「DFを抜きにいく」1対1の状況
                // プレイヤーのドリブル能力をボーナスとする
                riskBonus = player.Stats.Dribble / 2f; // Dribble:100 なら +50点
            }

            // 3. ターゲット
            float targetX = enemyGoalX;
            float targetY = enemyGoalY;

            if (riskBonus <= 0f && nearestOpponent != null) // 1v1 または 回避
            {
                // 相手のY座標を見て、逆サイドへ抜ける
                targetY = (nearestOpponent.Y < CENTER_Y) ? enemyGoalY + 80f : enemyGoalY - 80f;
            }

            // 4. 最終スコア
            // ★★★ エラー修正: (double) -> (float) ★★★
            float dribbleScore = goalBonus + riskBonus + (float)(_random.NextDouble() - 0.5) * 50f; // ランダムノイズ

            return (targetX, targetY, dribbleScore);
        }


        private void MakePass(PlayerData player, PlayerData targetPlayer, bool isLobbed = false)
        {
            // ★ Awayチームはロングボール（ロブ）を多用する
            if (player.Team == "away")
            {
                float xProgress = (player.Team == "home") ? (targetPlayer.X - player.X) : (player.X - targetPlayer.X);
                if (xProgress > 100f) // 100px以上の縦パスは
                {
                    isLobbed = true; // 強制的にロブパスにする
                }
            }

            const float basePassPower = 12f;
            float passPower = basePassPower * (player.Stats.Pass / 100f);
            float targetX = targetPlayer.X + targetPlayer.VX * 5;
            float targetY = targetPlayer.Y + targetPlayer.VY * 5;
            float distToTarget = Hypot(targetY - player.Y, targetX - player.X);

            float angle = (float)Math.Atan2(targetY - player.Y, targetX - player.X);
            _gameState.Ball.VX = (float)Math.Cos(angle) * passPower * GLOBAL_SPEED_FACTOR;
            _gameState.Ball.VY = (float)Math.Sin(angle) * passPower * GLOBAL_SPEED_FACTOR;

            _gameState.Ball.VZ = isLobbed ? 5 + (distToTarget / 50f) : 0;

            player.IsBallHolder = false;
            _currentHolderId = null;
            _closestPlayerToBall = null;
            Debug.WriteLine($"[Server AI] {player.DisplayName} passed to {targetPlayer.DisplayName} (Lob: {isLobbed})");
        }

        // ★ AI: メインロジック (期待値モデル + 積極的プレス)
        public void UpdateAI()
        {
            float minDistance = float.MaxValue;
            string closestPlayerId = null;
            string newHolderId = null;

            // 1. Find closest player to ball
            foreach (var player in _gameState.Players.Values)
            {
                float distance = Hypot(player.X - _gameState.Ball.X, player.Y - _gameState.Ball.Y);
                if (distance < minDistance)
                {
                    minDistance = distance;
                    closestPlayerId = player.Id;
                }
            }

            // 2. Determine ball holder (with Z height check)
            if (minDistance < PLAYER_KICK_RANGE)
            {
                if (string.IsNullOrEmpty(closestPlayerId)) return;
                var closer = _gameState.Players[closestPlayerId];

                PlayerData current = null;
                if (!string.IsNullOrEmpty(_currentHolderId))
                {
                    _gameState.Players.TryGetValue(_currentHolderId, out current);
                }

                bool canTouch = (closer.Role == "GK") ?
                                (_gameState.Ball.Z < GK_CATCH_HEIGHT) :
                                (_gameState.Ball.Z < PLAYER_KICK_HEIGHT);

                if (canTouch)
                {
                    if (current != null && current.Role == "GK" && current.Team != closer.Team && _gameState.Ball.Z < GK_CATCH_HEIGHT)
                    {
                        newHolderId = _currentHolderId; // GK invincible logic
                    }
                    else
                    {
                        newHolderId = closestPlayerId;
                    }
                }
                else newHolderId = null;
            }
            else newHolderId = null;

            _currentHolderId = newHolderId;
            _closestPlayerToBall = closestPlayerId;

            foreach (var player in _gameState.Players.Values)
            {
                player.IsBallHolder = (player.Id == newHolderId);
                if (player.IsBallHolder) _lastScorer = player.Id;
            }

            // ボールホルダーの参照をAIロジックのループ「前」に正しく取得
            PlayerData holder = null;
            bool teamHasBall = !string.IsNullOrEmpty(_currentHolderId)
                               && _gameState.Players.TryGetValue(_currentHolderId, out holder);

            // ★★★ NEW: 賢いプレス ロジック (ループ前) ★★★
            HashSet<string> pressers = new HashSet<string>();
            if (teamHasBall) // どちらかのチームがボールを持っている
            {
                string defendingTeam = (holder.Team == "home") ? "away" : "home";

                // ボールが「守備側」の陣地にあるか？
                // (例: Homeがホルダーなら、Ball.X > 400 か？)
                bool isBallInDefendingHalf = (holder.Team == "home") ?
                    (_gameState.Ball.X > FIELD_WIDTH / 2f) :
                    (_gameState.Ball.X < FIELD_WIDTH / 2f);

                if (isBallInDefendingHalf) // ★通常状態：ボールが敵陣にある
                {
                    // 敵陣なので、MFとFW全員でプレス
                    foreach (var p in _gameState.Players.Values)
                    {
                        if (p.Team == defendingTeam && (p.Role.StartsWith("MF") || p.Role.StartsWith("FW")))
                        {
                            pressers.Add(p.Id);
                        }
                    }
                    // ボールに近いDF 1名も参加
                    var closestDF = _gameState.Players.Values
                        .Where(p => p.Team == defendingTeam && p.Role.StartsWith("DF"))
                        .OrderBy(p => Hypot(p.X - holder.X, p.Y - holder.Y))
                        .FirstOrDefault();
                    if (closestDF != null) pressers.Add(closestDF.Id);
                }
                else // ★膠着状態：ボールが自陣（攻撃側）にある
                {
                    // 自陣でのパス回し。FWだけがプレスし、MF/DFは自陣でゾーンを守る
                    foreach (var p in _gameState.Players.Values)
                    {
                        if (p.Team == defendingTeam && p.Role.StartsWith("FW"))
                        {
                            pressers.Add(p.Id);
                        }
                    }
                }
            }
            // ★★★ 積極的プレス ロジックここまで ★★★


            // 4. AI Action Logic
            foreach (var player in _gameState.Players.Values)
            {
                if (player?.Stats == null) continue;

                string myTeam = player.Team;
                float enemyGoalX = (myTeam == "home") ? FIELD_WIDTH : 0;
                float enemyGoalY = CENTER_Y;
                float myGoalX = (myTeam == "home") ? 0 : FIELD_WIDTH;

                float[] basePos = null;
                if (myTeam == "home") HOME_POSITIONS.TryGetValue(player.Id, out basePos);
                else AWAY_POSITIONS.TryGetValue(player.Id, out basePos);

                float distToBall = Hypot(player.X - _gameState.Ball.X, player.Y - _gameState.Ball.Y);

                // チームのボール保持状況を正しくチェック
                bool myTeamHasBall = (teamHasBall && holder != null && holder.Team == myTeam);

                // --- Ball Holder Logic ---
                if (player.IsBallHolder)
                {
                    // GK
                    if (player.Role == "GK")
                    {
                        var gkPassDecision = FindBestPassTarget(player, new[] { "DF", "MF", "FW" });
                        if (gkPassDecision.target != null)
                        {
                            bool isLobbed = gkPassDecision.target.Role.StartsWith("FW");
                            MakePass(player, gkPassDecision.target, isLobbed);
                        }
                        else // Clear ball
                        {
                            float angle = (float)Math.Atan2(CENTER_Y - player.Y, enemyGoalX - player.X);
                            _gameState.Ball.VX = (float)Math.Cos(angle) * 12;
                            _gameState.Ball.VY = (float)Math.Sin(angle) * 12;
                            _gameState.Ball.VZ = 10;
                            player.IsBallHolder = false; _currentHolderId = null; _closestPlayerToBall = null;
                        }
                        continue;
                    }

                    // 1. Shot Check
                    float distanceToGoal = Hypot(player.X - enemyGoalX, player.Y - enemyGoalY);
                    float shotRange = PLAYER_SHOT_RANGE_DEFAULT * (player.Stats.ShotRangeMult);
                    if (distanceToGoal < shotRange)
                    {
                        float baseShotPower = 18f;
                        float shotPower = baseShotPower * (player.Stats.Shot / 100f) * (player.Stats.ShotMult);
                        float targetYAdjust = (float)(_random.NextDouble() - 0.5) * (GOAL_POST_Y_BOTTOM - GOAL_POST_Y_TOP);
                        float angleAdjusted = (float)Math.Atan2((enemyGoalY + targetYAdjust) - player.Y, enemyGoalX - player.X);
                        _gameState.Ball.VX = (float)Math.Cos(angleAdjusted) * shotPower * GLOBAL_SPEED_FACTOR;
                        _gameState.Ball.VY = (float)Math.Sin(angleAdjusted) * shotPower * GLOBAL_SPEED_FACTOR;
                        _gameState.Ball.VZ = 2; // Low shot
                        player.IsBallHolder = false; _currentHolderId = null; _closestPlayerToBall = null;
                        continue;
                    }

                    // ★★★ 期待値ベースの判断 (Dribble vs Pass) ★★★

                    // 2. Evaluate Dribble
                    var nearestOpponent = FindNearestOpponent(player);
                    var dribbleDecision = EvaluateDribble(player, nearestOpponent);

                    // 3. Evaluate Pass
                    var passDecision = FindBestPassTarget(player, new[] { "FW", "MF" });

                    // 4. Compare Scores
                    if (dribbleDecision.score > passDecision.score)
                    {
                        // ★ 決定: ドリブル (リスクテイク) ★
                        player.TargetX = dribbleDecision.targetX;
                        player.TargetY = dribbleDecision.targetY;
                        Debug.WriteLine($"[Server AI] {player.DisplayName} chose Dribble (Score: {dribbleDecision.score}) over Pass (Score: {passDecision.score})");
                        continue;
                    }
                    else
                    {
                        // ★ 決定: パス ★
                        if (passDecision.target != null)
                        {
                            MakePass(player, passDecision.target);
                            Debug.WriteLine($"[Server AI] {player.DisplayName} chose Pass (Score: {passDecision.score}) over Dribble (Score: {dribbleDecision.score})");
                        }
                        else
                        {
                            // パスもダメだった (フォールバック: 回避ドリブル)
                            player.TargetX = dribbleDecision.targetX;
                            player.TargetY = dribbleDecision.targetY;
                        }
                        continue;
                    }
                    // ★★★ 期待値ベースの判断ここまで ★★★
                }
                // --- Non-Ball Holder Logic ---
                else
                {
                    // GK
                    if (player.Role == "GK")
                    {
                        player.TargetX = (myTeam == "home") ? GOAL_LINE_X_HOME + 20 : GOAL_LINE_X_AWAY - 20;
                        player.TargetY = Math.Max(GOAL_POST_Y_TOP, Math.Min(GOAL_POST_Y_BOTTOM, _gameState.Ball.Y));
                        continue;
                    }

                    // (フィールドプレイヤー: DF, MF, FW)
                    if (basePos != null)
                    {
                        // 1. 基本となる「ゾーン」の目標位置を計算
                        float targetX, targetY;
                        float ballOffsetX = (_gameState.Ball.X - (FIELD_WIDTH / 2f));
                        float ballOffsetY = (_gameState.Ball.Y - CENTER_Y);

                        // デフォルトの守備ゾーン
                        float zoneShiftFactor = 0.3f; // DF
                        if (player.Role.StartsWith("MF")) zoneShiftFactor = 0.5f; // MF
                        else if (player.Role.StartsWith("FW")) zoneShiftFactor = 0.2f; // FW
                        targetX = basePos[0] + ballOffsetX * zoneShiftFactor;
                        targetY = basePos[1] + ballOffsetY * zoneShiftFactor;

                        // ★★★ オフザボールの戦術ロジック (チーム別) ★★★
                        if (myTeamHasBall)
                        {
                            bool isBallAdvanced = (myTeam == "home") ? (_gameState.Ball.X > FIELD_WIDTH / 2f) : (_gameState.Ball.X < FIELD_WIDTH / 2f);

                            // --- Home (4-2-3-1 ポゼッション/オーバーラップ) ---
                            if (myTeam == "home")
                            {
                                float offensivePush = 100f;

                                // FW (player10)
                                if (player.Role.StartsWith("FW"))
                                {
                                    // ★ FW裏抜け: 自分のレーン(basePos[1])の裏へ
                                    float runTargetX = (holder.X > basePos[0]) ? holder.X + 100f : basePos[0] + 100f; // ホルダーより前、または初期位置より前
                                    targetX = Math.Min(runTargetX, FIELD_WIDTH - 120f);
                                    targetY = basePos[1]; // 自分のレーン
                                }
                                // 攻撃的MF (player7, 8, 9)
                                else if (player.Role.StartsWith("MF") && (player.Id == "player7" || player.Id == "player8" || player.Id == "player9"))
                                {
                                    // ★ 2列目からの飛び出し: 自分のレーンの裏へ
                                    float runTargetX = (holder.X > basePos[0]) ? holder.X + 50f : basePos[0] + 50f;
                                    targetX = Math.Min(runTargetX, FIELD_WIDTH - 150f);
                                    targetY = basePos[1]; // 自分のレーン
                                }
                                // サイドバック (player1, 4)
                                else if (player.Role.StartsWith("DF") && (player.Id == "player1" || player.Id == "player4"))
                                {
                                    // ★ オーバーラップロジック (ボールの位置基準)
                                    bool isBallOnMyWing = (basePos[1] < CENTER_Y) ? (_gameState.Ball.Y < CENTER_Y - 50f) : (_gameState.Ball.Y > CENTER_Y + 50f);

                                    if (isBallAdvanced && isBallOnMyWing) // ボールが敵陣の自分のサイドにある
                                    {
                                        // ★オーバーラップ実行★
                                        targetX = FIELD_WIDTH - 250f; // 敵陣深くまで上がる
                                        targetY = basePos[1];
                                    }
                                    else
                                    {
                                        // ラインを上げるだけ
                                        targetX = basePos[0] + offensivePush + (ballOffsetX * 0.2f);
                                        targetY = basePos[1] + (ballOffsetY * 0.4f);
                                    }
                                }
                                // ボランチとCB (player2, 3, 5, 6)
                                else
                                {
                                    // 後方でサポート
                                    float supportTargetX = holder.X - 100f;
                                    targetX = Math.Max(supportTargetX, basePos[0]);
                                    targetY = basePos[1] + (holder.Y - basePos[1]) * 0.3f;
                                }
                            }
                            // --- Away (4-3-3 カウンター) ---
                            else
                            {
                                // FWs (19, 20, 21)
                                if (player.Role.StartsWith("FW"))
                                {
                                    // ★カウンターラン★ 常に最前線へ
                                    targetX = 120f;
                                    targetY = basePos[1]; // 自分のレーン (LW, CF, RW)
                                }
                                // MFs (16, 17, 18)
                                else if (player.Role.StartsWith("MF"))
                                {
                                    // ★カウンターサポート★ FWに続く
                                    targetX = 200f;
                                    targetY = basePos[1]; // 自分のレーン
                                }
                                // DFs (12, 13, 14, 15)
                                else
                                {
                                    // 4バックはラインを上げる (3バックより少し攻撃的)
                                    targetX = basePos[0] + ballOffsetX * 0.3f;
                                    targetY = basePos[1] + ballOffsetY * 0.3f;
                                }
                            }
                        }
                        // ★★★ オフザボールの戦術ロジックここまで ★★★


                        // 2. 「ボールを追う」べきか判断
                        bool shouldChaseBall = false;

                        if (myTeamHasBall)
                        {
                            // 攻撃時は、ボールを追わない
                            shouldChaseBall = false;
                        }
                        else // 守備時・ルーズボール時
                        {
                            // 2a. ★★★ 積極的プレス ★★★
                            // （相手がボールを持っている）
                            if (_currentHolderId != null)
                            {
                                // この選手は、選ばれたプレッサーか？
                                if (pressers.Contains(player.Id))
                                {
                                    shouldChaseBall = true;
                                }
                            }
                            // 2b. ルーズボール時 (誰も持っていない)
                            else
                            {
                                // この選手が「フィールドで最もボールに近い選手」か？
                                if (player.Id == _closestPlayerToBall)
                                {
                                    shouldChaseBall = true;
                                }
                            }
                        }

                        // 3. 最終的なターゲットを決定
                        if (shouldChaseBall)
                        {
                            player.TargetX = _gameState.Ball.X;
                            player.TargetY = _gameState.Ball.Y;
                        }
                        else
                        {
                            // ゾーンを維持する
                            player.TargetX = targetX;
                            player.TargetY = targetY;
                        }
                    }
                    else
                    {
                        // (フォールバック)
                        player.TargetX = _gameState.Ball.X;
                        player.TargetY = _gameState.Ball.Y;
                    }
                }
            }
        }

        // --- ★★★ UpdateGameのメインロジック (エラー修正済み) ★★★
        public void UpdateGame()
        {
            if (_isPaused) return;

            try
            {
                // 試合状況に応じて処理を分岐
                switch (_gameState.MatchStatus)
                {
                    case "Playing":
                        // 試合中
                        UpdateAI();
                        UpdatePhysics();

                        // ★★★ 60フレームに1回、時間を進める ★★★
                        _frameCounter++;
                        if (_frameCounter >= 60)
                        {
                            _frameCounter = 0;
                            if (_gameState.Time > 0)
                            {
                                _gameState.Time--;
                            }
                        }
                        // ★★★ 修正ここまで ★★★

                        // 時間が0になったら試合終了
                        if (_gameState.Time <= 0)
                        {
                            // ★ 試合終了処理
                            _gameState.Time = 0;
                            _gameState.MatchStatus = "MatchEnd";
                            _gameState.MatchEnded = true; // 互換性のため
                            _gameState.GoalMessage = "TIME UP";
                            _pauseTimer = 300; // 5秒 (Continueボタンが出るまでの待機)
                        }
                        break;

                    case "GoalScored":
                        // ゴール後のポーズ
                        _pauseTimer--;
                        if (_pauseTimer <= 0)
                        {
                            // ポーズ終了後、キックオフ準備
                            ResetForKickoff(true); // 選手を配置し、キックオフ
                            _gameState.MatchStatus = "Playing"; // 試合再開
                        }
                        break;

                    case "MatchEnd":
                        // 試合終了後の待機
                        _pauseTimer--;
                        if (_pauseTimer <= 0)
                        {
                            // "Continue" ボタンを表示させるための待機
                            // 特に何もしない (クライアントからの "CONTINUE_GAME" 待ち)
                        }
                        break;

                    case "WaitingToStart":
                    default:
                        // "Game Start" ボタンが押されるまで待機
                        // (AIも物理も動かさない)
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Server Error] Error in game loop: {ex.Message}");
                _isPaused = true;
            }
        }
    }
}