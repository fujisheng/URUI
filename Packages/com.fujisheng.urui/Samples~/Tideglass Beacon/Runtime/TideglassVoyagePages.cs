using URUI.Controls;

namespace URUI.Samples.Tideglass
{
    public sealed partial class TideglassDemo
    {
        int inspectedRoute;

        void BindVoyageDesk()
        {
            var destination = TideglassRoute.All[selectedRoute];
            page.Q<Image>("mission-art").Source = TideglassAssets.ImageSource(destination.Art);
            page.Q("mission-name").Text = destination.Name;
            page.Q("mission-index").Text = $"航线 0{selectedRoute + 1} / 03";
            Bind("chart", () => Request(Page.Chart));
            Bind("launch", () => Depart(selectedRoute));
            Bind("loadout", () => Request(Page.Loadout));
            Bind("range", () => Request(Page.Range));
            Bind("debrief", () => Request(Page.Debrief));
            page.Q("current-order").Text = FlightPlan.Orders[(int)profile.Order];
            page.Q("brief-state").Text = profile.LastFlight == null ? (profile.LitCount == 0 ? "待首航" : "待新航程") : TideglassRoute.All[profile.LastFlight.route].Name;
            page.Q("drill-count").Text = $"{profile.PassedDrills} / {FlightPlan.DrillGoals.Length}";
            BindWatchDesk();
            int pending = profile.ClaimableCount;
            page.Q("ledger-count").Text = pending > 0 ? $"{pending} 可领" : $"{profile.LitCount} / 3";
            page.Q("logbook").EnableInClassList("available", pending > 0);
            Bind("logbook", () => { showRecords = false; Request(Page.Logbook); });
        }

        void Depart(int route) => BeginFlight(route, false);

        void BeginFlight(int route, bool practice)
        {
            if ((uint)route >= TideglassRoute.All.Length || (!practice && !profile.Open(route)))
                return;
            practiceFlight = practice;
            flownRoute = route;
            if (!practice) selectedRoute = route;
            var destination = TideglassRoute.All[route];
            var images = new string[TrialImages.Length + 1];
            images[0] = practice ? "range" : destination.Art;
            System.Array.Copy(TrialImages, 0, images, 1, TrialImages.Length);
            StartLoading(Page.Trial, images, practice ? "接入演练靶场" : "跃迁至" + destination.Name);
        }

        void BindChart()
        {
            WriteBalance();
            for (int i = 0; i < TideglassRoute.All.Length; i++)
            {
                int route = i;
                Bind("node-" + route, () => InspectRoute(route));
                page.Q("node-name-" + i).Text = TideglassRoute.All[i].Name;
                page.Q("node-" + i).EnableInClassList("uncharted", !profile.Open(i));
                page.Q("node-" + i).EnableInClassList("lit", profile.Clears(i) > 0);
            }
            Bind("depart", () => Depart(inspectedRoute));
            Bind("chart-log", () => { showRecords = true; Request(Page.Logbook); });
            InspectRoute(inspectedRoute);
        }

        void InspectRoute(int route)
        {
            inspectedRoute = route;
            bool open = profile.Open(route);
            var d = TideglassRoute.All[route];
            page.Q<Image>("route-art").Source = TideglassAssets.ImageSource(d.Art);
            page.Q("route-name").Text = d.Name;
            page.Q("route-index").Text = $"航区 0{route + 1} / 03";
            page.Q("route-target").Text = $"{d.Target}   /   {d.Hull}";
            page.Q("route-brief").Text = d.Hint;
            page.Q("route-pay").Text = $"潮晶 +{d.Pay}";
            page.Q("route-record").Text = open ? (profile.Clears(route) > 0 ? "最快 " + TideglassRoute.TimeLabel(profile.Best(route)) : "未点亮")
                : "需点亮" + TideglassRoute.All[route - 1].Name;
            page.Q<Button>("depart").SetEnabled(open);
            page.Q("depart").Q(className: "control-inner").Text = open ? "前往信标" : "航道未开放";
            for (int i = 0; i < TideglassRoute.All.Length; i++)
            {
                var node = page.Q<Button>("node-" + i);
                node.EnableInClassList("selected", i == route);
                node.SetAttribute("aria-pressed", i == route ? "true" : "false");
            }
            Animate("Sounding");
        }

        void BindLogbook()
        {
            Bind("tab-contracts", () => SetFolio(false));
            Bind("tab-records", () => SetFolio(true));
            Bind("book-chart", () => Request(Page.Chart));
            for (int i = 0; i < TideglassProfile.Bounties.Length; i++)
            {
                int contract = i;
                Bind("claim-" + i, () => ClaimContract(contract));
            }
            RefreshFolio();
            SetFolio(showRecords);
        }

        void SetFolio(bool records)
        {
            showRecords = records;
            page.Q("contracts").EnableInClassList("gone", records);
            page.Q("records").EnableInClassList("gone", !records);
            page.Q("tab-contracts").EnableInClassList("selected", !records);
            page.Q("tab-records").EnableInClassList("selected", records);
            page.Q("tab-contracts").SetAttribute("aria-pressed", records ? "false" : "true");
            page.Q("tab-records").SetAttribute("aria-pressed", records ? "true" : "false");
            Animate("Folio");
        }

        void RefreshFolio()
        {
            WriteBalance();
            page.Q("book-index").Text = $"{profile.LitCount} / 3";
            for (int i = 0; i < TideglassRoute.All.Length; i++)
            {
                int count = profile.Clears(i);
                page.Q("record-name-" + i).Text = TideglassRoute.All[i].Name;
                page.Q("record-log-" + i).Text = count > 0 ? $"点亮 {count} 次" : "尚无航迹";
                page.Q("record-best-" + i).Text = TideglassRoute.TimeLabel(profile.Best(i));
                page.Q("contract-name-" + i).Text = TideglassProfile.ContractNames[i];
                page.Q("contract-progress-" + i).Text = TideglassProfile.ContractBriefs[i] + $"   {profile.Progress(i)}/{TideglassProfile.Goal(i)}";
                page.Q("bounty-" + i).Text = "+" + TideglassProfile.Bounties[i];
                var claim = page.Q<Button>("claim-" + i);
                claim.SetEnabled(profile.CanClaim(i));
                claim.Text = profile.Claimed(i) ? "已签收" : profile.CanClaim(i) ? "领取" : "进行中";
                page.Q("contract-" + i).EnableInClassList("redeemed", profile.Claimed(i));
            }
        }

        void ClaimContract(int contract)
        {
            if (!profile.Claim(contract))
                return;
            RefreshFolio();
            PlayAudio(reward);
            Notice($"{TideglassProfile.ContractNames[contract]} · 潮晶 +{TideglassProfile.Bounties[contract]}");
            Animate("Signed");
        }
    }
}
