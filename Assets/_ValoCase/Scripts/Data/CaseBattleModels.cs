using System.Collections.Generic;

namespace ValoCase.Data
{
    /// <summary>
    /// Runtime-only Case Battle lobby data models.
    /// NOT persisted — these never touch the save system. The flow is in-memory
    /// for the lobby UX (create / wait / join). Reward resolution remains the
    /// responsibility of the protected CaseBattleSystem.
    /// </summary>
    public enum BattleMode
    {
        Normal,
        Crazy
    }

    public enum BattlePlayerCount
    {
        OneVOne     = 2,
        ThreePlayer = 3,
        FourPlayer  = 4
    }

    public enum LobbyStatus
    {
        Waiting,
        Full,
        Live
    }

    public enum PlayerSlotType
    {
        Filled,
        EmptyWaiting,
        EmptyAddBot,
        Bot
    }

    public sealed class BattleLobbyData
    {
        public string            LobbyId;
        public string            HostName;
        public string            CaseName;
        public int               Rounds = 1;
        public BattleMode         Mode  = BattleMode.Normal;
        public BattlePlayerCount  PlayerCount = BattlePlayerCount.OneVOne;
        public int               CurrentPlayers = 1;
        public LobbyStatus        Status = LobbyStatus.Waiting;
        public SkinRarity         Rarity = SkinRarity.Select;
        public int               WagerVP;

        public int MaxPlayers => (int)PlayerCount;

        public LobbyStatus ComputeStatus()
        {
            if (Status == LobbyStatus.Live) return LobbyStatus.Live;
            return CurrentPlayers >= MaxPlayers ? LobbyStatus.Full : LobbyStatus.Waiting;
        }
    }

    public sealed class BattlePlayerData
    {
        public string         Username;
        public bool           IsHost;
        public bool           IsLocalPlayer;
        public bool           IsBot;
        public PlayerSlotType SlotType = PlayerSlotType.EmptyWaiting;

        public static BattlePlayerData Empty(bool hostCanAddBot)
            => new BattlePlayerData
            {
                SlotType = hostCanAddBot ? PlayerSlotType.EmptyAddBot : PlayerSlotType.EmptyWaiting
            };

        public static BattlePlayerData MakeBot(string name)
            => new BattlePlayerData
            {
                Username = name,
                IsBot    = true,
                SlotType = PlayerSlotType.Bot
            };
    }

    /// <summary>Simple in-memory provider of demo lobbies for the list screen.</summary>
    public static class CaseBattleSampleData
    {
        public static List<BattleLobbyData> BuildSampleLobbies()
        {
            return new List<BattleLobbyData>
            {
                new BattleLobbyData {
                    LobbyId = "A3F9", HostName = "Phantom", CaseName = "Vandal Vault",
                    Rounds = 2, Mode = BattleMode.Crazy, PlayerCount = BattlePlayerCount.FourPlayer,
                    CurrentPlayers = 3, Status = LobbyStatus.Waiting, Rarity = SkinRarity.Ultra, WagerVP = 3800
                },
                new BattleLobbyData {
                    LobbyId = "B7K2", HostName = "Reyna", CaseName = "Operator Elite",
                    Rounds = 1, Mode = BattleMode.Normal, PlayerCount = BattlePlayerCount.OneVOne,
                    CurrentPlayers = 1, Status = LobbyStatus.Waiting, Rarity = SkinRarity.Exclusive, WagerVP = 1200
                },
                new BattleLobbyData {
                    LobbyId = "C1M5", HostName = "Jett", CaseName = "Sheriff Prime",
                    Rounds = 3, Mode = BattleMode.Normal, PlayerCount = BattlePlayerCount.FourPlayer,
                    CurrentPlayers = 4, Status = LobbyStatus.Full, Rarity = SkinRarity.Premium, WagerVP = 5400
                },
                new BattleLobbyData {
                    LobbyId = "D9X1", HostName = "Sova", CaseName = "Spectre Mix",
                    Rounds = 1, Mode = BattleMode.Normal, PlayerCount = BattlePlayerCount.ThreePlayer,
                    CurrentPlayers = 2, Status = LobbyStatus.Live, Rarity = SkinRarity.Deluxe, WagerVP = 900
                },
            };
        }
    }
}
