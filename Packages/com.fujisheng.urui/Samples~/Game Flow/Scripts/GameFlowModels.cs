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
        public int level;
        public int credits;
        public int power;
        public int rank;
        public PlayerProfile Clone() => (PlayerProfile)MemberwiseClone();
    }

    [Serializable]
    public sealed class InventoryItem
    {
        public string id;
        public string name;
        public string category;
        public string description;
        public string rarity;
        public string icon;
        public int count;
        public int power;
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
