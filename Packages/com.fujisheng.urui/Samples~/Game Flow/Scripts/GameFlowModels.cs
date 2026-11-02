using System;

namespace URUI.Samples.GameFlow
{
    [Serializable]
    public sealed class GameCatalog
    {
        public PlayerProfile player;
        public InventoryItem[] inventory;
        public RankingEntry[] rankings;
    }

    [Serializable]
    public sealed class PlayerProfile
    {
        public string callSign;
        public int level, credits, power, rank;
        public PlayerProfile Clone() => (PlayerProfile)MemberwiseClone();
    }

    [Serializable]
    public sealed class InventoryItem
    {
        public string id, name, category, description, rarity, icon;
        public int count, power;
        public bool equipped;
        public InventoryItem Clone() => (InventoryItem)MemberwiseClone();
    }

    [Serializable]
    public sealed class RankingEntry
    {
        public string name;
        public int score;
    }

    [Serializable]
    public sealed class GameSettings
    {
        public float volume = .65f;
        public bool sound = true;
        public bool compactRows;
        public string accent = "Amber";
        public bool offline;
        public GameSettings Clone() => (GameSettings)MemberwiseClone();
    }
}
