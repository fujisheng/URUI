using URUI.Samples.GameFlow;

int checks = 0;
void Need(bool value, string name) { if (!value) throw new Exception(name); checks++; }
async Task Reject(Func<Task> call, string name) { try { await call(); } catch (InvalidOperationException) { checks++; return; } throw new Exception(name); }
var catalog = new GameCatalog {
    player = new PlayerProfile { callSign = "Pilot", level = 12, credits = 4200, power = 1600, rank = 27 },
    inventory = new[] {
        new InventoryItem { id="blade", name="Arc blade", category="Equipment", count=1, power=120 },
        new InventoryItem { id="armor", name="Flight armor", category="Equipment", count=1, power=80 },
        new InventoryItem { id="medkit", name="Field medkit", category="Consumables", count=2 },
        new InventoryItem { id="core", name="Ion core", category="Materials", count=4 } },
    rankings = new[] { new RankingEntry { name="Nova", score=5000 }, new RankingEntry { name="Sol", score=4000 } }
};
var service = new MockGameService(catalog, 1);
service.FailNextInitialization = true;
await Reject(() => service.InitializeAsync(default), "startup failure must surface");
await service.InitializeAsync(default); Need(service.IsInitialized, "initialization retry");
await Reject(() => service.LoginAsync(" ", "demo", default), "blank call sign");
await Reject(() => service.LoginAsync("Pilot", "wrong", default), "invalid demo access code");
service.IsOffline = true;
await Reject(() => service.LoginAsync("Pilot", "demo", default), "offline login");
service.IsOffline = false;
var player = await service.LoginAsync("  Rhea  ", "demo", default);
Need(player.callSign == "Rhea", "trimmed login");
Need(service.FilterInventory("Materials", "ion").Single().id == "core", "filter preserves identity");
Need(service.FilterInventory("All", "no-match").Count == 0, "empty inventory filter");
service.UseOrEquipItem("blade"); Need(service.Inventory.Single(x=>x.id=="blade").equipped && service.CurrentPlayer.power == 1720, "equip updates player");
service.UseOrEquipItem("armor"); Need(!service.Inventory.Single(x=>x.id=="blade").equipped && service.CurrentPlayer.power == 1680, "equip replaces previous gear");
service.UseOrEquipItem("medkit"); service.UseOrEquipItem("medkit");
await Reject(() => { service.UseOrEquipItem("medkit"); return Task.CompletedTask; }, "depleted consumable");
Need(service.FilterInventory("Consumables", "").Count == 0, "consumed items leave visible inventory");
Need((await service.GetRankingsAsync("Friends", default)).Count == 0, "empty friends feed");
var first = await service.GetRankingsAsync("Global", default); var second = await service.GetRankingsAsync("Global", default);
Need(first[0].score < second[0].score, "refresh returns changed snapshot");
service.IsOffline = true; await Reject(() => service.GetRankingsAsync("Global", default), "offline leaderboard error");
service.IsOffline = false; Need((await service.GetRankingsAsync("Global", default)).Count == 2, "leaderboard retry");
using (var cancellation = new CancellationTokenSource()) {
    cancellation.Cancel();
    try { await service.GetRankingsAsync("Global", cancellation.Token); throw new Exception("cancelled request accepted"); }
    catch (OperationCanceledException) { checks++; }
}
var settings = new GameSettings(); var draft = settings.Clone(); draft.volume = .1f;
Need(settings.volume != draft.volume, "draft must not mutate applied settings");
Console.WriteLine($"GAME_FLOW_MODEL_PASS checks={checks}");
