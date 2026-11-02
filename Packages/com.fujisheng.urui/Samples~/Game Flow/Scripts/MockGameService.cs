using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace URUI.Samples.GameFlow
{
    /// <summary>Local demonstration data only. No network, authentication or real account.</summary>
    public sealed class MockGameService
    {
        readonly GameCatalog catalog;
        readonly List<InventoryItem> inventory = new List<InventoryItem>();
        readonly int latencyMilliseconds;
        readonly int basePower;
        int refresh;
        public bool IsOffline { get; set; }
        public bool FailNextInitialization { get; set; }
        public bool IsInitialized { get; private set; }
        public PlayerProfile CurrentPlayer { get; private set; }
        public IReadOnlyList<InventoryItem> Inventory => inventory;

        public MockGameService(GameCatalog catalog, int latencyMilliseconds = 350)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (catalog.player == null || catalog.inventory == null || catalog.rankings == null)
                throw new ArgumentException("The sample catalog is incomplete.");
            this.latencyMilliseconds = Math.Max(0, latencyMilliseconds);
            basePower = catalog.player.power;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in catalog.inventory)
            {
                if (item == null || string.IsNullOrEmpty(item.id) || !ids.Add(item.id) || item.count < 0)
                    throw new ArgumentException("The sample inventory contains an invalid or duplicate item.");
                inventory.Add(item.Clone());
            }
        }

        async Task Wait(CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            await Task.Delay(latencyMilliseconds, cancellation);
            cancellation.ThrowIfCancellationRequested();
        }

        public async Task InitializeAsync(CancellationToken cancellation)
        {
            await Wait(cancellation);
            if (FailNextInitialization)
            {
                FailNextInitialization = false;
                throw new InvalidOperationException("Local catalog initialization was interrupted. Retry to continue.");
            }
            IsInitialized = true;
        }

        public async Task<PlayerProfile> LoginAsync(string callSign, string accessCode, CancellationToken cancellation)
        {
            if (!IsInitialized) throw new InvalidOperationException("Initialize the sample first.");
            callSign = callSign?.Trim() ?? "";
            if (callSign.Length < 2 || callSign.Length > 18)
                throw new InvalidOperationException("Enter a call sign between 2 and 18 characters.");
            await Wait(cancellation);
            if (IsOffline) throw new InvalidOperationException("The simulated service is offline. Choose Online and retry.");
            if (accessCode != "demo") throw new InvalidOperationException("This is a local demo. The access code is demo.");
            CurrentPlayer = catalog.player.Clone();
            CurrentPlayer.callSign = callSign;
            RecalculatePower();
            return CurrentPlayer;
        }

        public List<InventoryItem> FilterInventory(string category, string query)
        {
            var result = new List<InventoryItem>();
            foreach (var item in inventory)
                if (item.count > 0 && (category == "All" || category == item.category)
                    && (string.IsNullOrWhiteSpace(query) || item.name.IndexOf(query.Trim(), StringComparison.OrdinalIgnoreCase) >= 0))
                    result.Add(item);
            return result;
        }

        public string UseOrEquipItem(string id)
        {
            var item = inventory.Find(value => value.id == id);
            if (CurrentPlayer == null || item == null || item.count <= 0)
                throw new InvalidOperationException("This item is no longer available.");
            if (item.category == "Equipment")
            {
                bool equip = !item.equipped;
                foreach (var other in inventory) other.equipped = false;
                item.equipped = equip;
                RecalculatePower();
                return item.name + (equip ? " equipped." : " unequipped.");
            }
            if (item.category == "Consumables")
            {
                item.count--;
                return item.name + " used. This local demonstration has no combat simulation.";
            }
            return "Crafting materials are read-only in this sample.";
        }

        void RecalculatePower()
        {
            CurrentPlayer.power = basePower;
            foreach (var item in inventory) if (item.equipped) CurrentPlayer.power += item.power;
        }

        public async Task<IReadOnlyList<RankingEntry>> GetRankingsAsync(string scope, CancellationToken cancellation)
        {
            await Wait(cancellation);
            if (IsOffline) throw new InvalidOperationException("The simulated ranking service is offline. Open Settings to reconnect.");
            var result = new List<RankingEntry>();
            if (scope == "Friends") return result;
            refresh++;
            foreach (var entry in catalog.rankings)
                result.Add(new RankingEntry { name = entry.name, score = entry.score + refresh * 7 });
            result.Sort((left, right) => right.score.CompareTo(left.score));
            return result;
        }
    }
}
