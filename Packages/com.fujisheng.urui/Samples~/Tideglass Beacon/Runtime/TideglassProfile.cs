using System;

namespace URUI.Samples.Tideglass
{
    internal enum Seal { Tide, Flame, Gale }

    internal static class TideglassCatalog
    {
        internal static readonly string[] Names = { "潮纹", "烬羽", "风棱" };
        internal static readonly string[] Descriptions = { "引潮额外积蓄 14 潮能", "裂光额外造成 24 伤害", "引潮与裂光冷却缩短 0.7 秒" };
        internal static readonly int[] Prices = { 0, 80, 140 };
        internal static readonly string[] SkillNames = { "引潮", "裂光", "潮鸣" };
        internal static readonly string[] Seals = { "module-0", "module-1", "module-2" };
    }

    internal sealed partial class TideglassProfile
    {
        internal static readonly string[] ContractNames = { "初航回执", "调谐工单", "全域点灯" };
        internal static readonly string[] ContractBriefs = { "完成一次信标试炼", "装配一个新购买的模组", "点亮三处不同的信标" };
        internal static readonly int[] Bounties = { 40, 35, 120 };

        readonly VoyageSave data;
        readonly Action<VoyageSave> persist;

        internal TideglassProfile(VoyageSave source = null, Action<VoyageSave> persist = null)
        {
            data = source != null && source.version == 1 ? source : new VoyageSave();
            this.persist = persist;
            data.balance = Math.Max(0, data.balance);
            data.ownedMask = (data.ownedMask & 7) | 1;
            if ((uint)data.equipped > 2 || !Owns((Seal)data.equipped))
                data.equipped = 0;
            data.claimedMask &= 7;
            data.activePreset = Math.Clamp(data.activePreset, 0, 1);
            if (data.presetSeals?.Length != 2)
                data.presetSeals = new[] { data.equipped, data.equipped };
            if (data.presetOrders?.Length != 2)
                data.presetOrders = new[] { 0, 1 };
            for (int i = 0; i < 2; i++)
            {
                if (!Owns((Seal)data.presetSeals[i])) data.presetSeals[i] = 0;
                if ((uint)data.presetOrders[i] >= FlightPlan.Orders.Length) data.presetOrders[i] = 0;
            }
            data.presetSeals[data.activePreset] = data.equipped;
            Array.Resize(ref data.drillBest, FlightPlan.DrillGoals.Length);
            if (data.lastFlight != null && !data.lastFlight.IsValid) data.lastFlight = null;
            Array.Resize(ref data.clears, TideglassRoute.All.Length);
            Array.Resize(ref data.bestMs, TideglassRoute.All.Length);
            for (int i = 0; i < data.clears.Length; i++)
            {
                data.clears[i] = Math.Max(0, data.clears[i]);
                data.bestMs[i] = data.clears[i] > 0 ? Math.Max(0, data.bestMs[i]) : 0;
            }
            RestoreWatch();
        }

        internal int Balance => data.balance;
        internal Seal Equipped => (Seal)data.equipped;
        internal int ActivePreset => data.activePreset;
        internal Seal PresetSeal(int slot) => (Seal)data.presetSeals[slot];
        internal FlightOrder PresetOrder(int slot) => (FlightOrder)data.presetOrders[slot];
        internal FlightOrder Order => PresetOrder(ActivePreset);
        internal FlightReport LastFlight => data.lastFlight;
        internal int DrillBest(int drill) => Math.Max(data.drillBest[drill], 0);
        internal int LatestOpen => Clears(0) == 0 ? 0 : Clears(1) == 0 ? 1 : 2;
        internal int LitCount => Math.Sign(Clears(0)) + Math.Sign(Clears(1)) + Math.Sign(Clears(2));
        internal int ClaimableCount => (CanClaim(0) ? 1 : 0) + (CanClaim(1) ? 1 : 0) + (CanClaim(2) ? 1 : 0);
        internal int Clears(int route) => data.clears[route];
        internal int Best(int route) => data.bestMs[route];
        internal bool Open(int route) => (uint)route < TideglassRoute.All.Length && route <= LatestOpen;
        internal bool Owns(Seal seal) => (uint)seal < 3 && (data.ownedMask & (1 << (int)seal)) != 0;

        internal bool TryBuy(Seal seal)
        {
            if ((uint)seal >= 3 || Owns(seal) || Balance < TideglassCatalog.Prices[(int)seal])
                return false;
            data.balance -= TideglassCatalog.Prices[(int)seal];
            data.ownedMask |= 1 << (int)seal;
            persist?.Invoke(data);
            return true;
        }

        internal bool TryEquip(Seal seal)
        {
            if (!Owns(seal))
                return false;
            if (Equipped != seal)
            {
                data.equipped = data.presetSeals[ActivePreset] = (int)seal;
                data.calibrated |= seal != Seal.Tide;
                persist?.Invoke(data);
            }
            return true;
        }

        internal bool ApplyPreset(int slot, Seal seal, FlightOrder order)
        {
            if ((uint)slot >= 2 || !Owns(seal) || (uint)order >= FlightPlan.Orders.Length)
                return false;
            data.activePreset = slot;
            data.equipped = data.presetSeals[slot] = (int)seal;
            data.presetOrders[slot] = (int)order;
            data.calibrated |= seal != Seal.Tide;
            persist?.Invoke(data);
            return true;
        }

        internal void CompleteDrill(int drill, double seconds)
        {
            int ms = Milliseconds(seconds);
            if (DrillBest(drill) > 0 && ms >= DrillBest(drill)) return;
            data.drillBest[drill] = ms;
            persist?.Invoke(data);
        }

        static int Milliseconds(double seconds) => (int)Math.Clamp(Math.Round(seconds * 1000), 1, int.MaxValue);

        internal void Complete(int route, double seconds, TrialSession session)
        {
            if (!Open(route))
                return;
            int ms = Milliseconds(seconds);
            data.lastFlight = session.Report(ms);
            data.clears[route] = (int)Math.Min(int.MaxValue, 1L + data.clears[route]);
            if (Best(route) == 0 || ms < Best(route))
                data.bestMs[route] = ms;
            Credit(TideglassRoute.All[route].Pay);
            persist?.Invoke(data);
        }

        internal bool Claimed(int index) => (data.claimedMask & (1 << index)) != 0;
        internal int Progress(int index) => index switch
        {
            0 => Math.Min(1, LitCount),
            1 => data.calibrated ? 1 : 0,
            _ => LitCount
        };
        internal static int Goal(int index) => index == 2 ? 3 : 1;
        internal bool CanClaim(int index) => !Claimed(index) && Progress(index) >= Goal(index);

        internal bool Claim(int index)
        {
            if ((uint)index >= Bounties.Length || !CanClaim(index))
                return false;
            data.claimedMask |= 1 << index;
            Credit(Bounties[index]);
            persist?.Invoke(data);
            return true;
        }

        void Credit(int amount) => data.balance = (int)Math.Min(int.MaxValue, (long)data.balance + amount);
    }

    internal sealed class TrialSession
    {
        internal const int MaxCharge = 100;
        internal const int FinisherCost = 60;
        readonly int route;
        readonly FlightOrder order;
        readonly bool training;
        readonly Wing escort;
        readonly int[] counts = new int[3], dealt = new int[3];
        int chain;
        int lastBasic = -1;

        readonly Seal seal;
        readonly double[] readyAt = new double[3];

        internal TrialSession(Seal seal, int route = 0, FlightOrder order = FlightOrder.Surge, bool training = false, Wing escort = Wing.None)
        {
            this.seal = seal;
            this.route = route;
            this.order = order;
            this.training = training;
            this.escort = escort;
            Charge = escort == Wing.Sounder ? TideglassWatch.InitialCharge : 0;
            Health = HealthCap;
            Shells = route == 1 ? 2 : 0;
        }

        internal int HealthCap => TideglassRoute.All[route].Hull;
        internal int Shells { get; private set; }
        internal bool ChainToLight => route == 2 && lastBasic == 0;
        internal string Phase => route switch
        {
            1 => Shells > 0 ? $"镜壳 ×{Shells} · 裂光可破壳" : "镜壳已碎",
            2 => lastBasic < 0 ? "交替施放引潮与裂光" : lastBasic == 0 ? "连携窗口 · 裂光" : "连携窗口 · 引潮",
            _ => "蓄能后释放潮鸣"
        };

        internal int Health { get; private set; }
        internal int Charge { get; private set; }
        internal int Casts { get; private set; }
        internal bool OrderUsed { get; private set; }
        internal int DrillProgress => route switch { 0 => counts[2], 1 => 2 - Shells, _ => chain };
        internal bool Won => training ? DrillProgress >= FlightPlan.DrillGoals[route] : Health == 0;

        internal bool CanIssueOrder(double now) => !OrderUsed && !Won &&
            (order == FlightOrder.Surge ? Charge < MaxCharge : Remaining(0, now) > 0 || Remaining(1, now) > 0);

        internal bool IssueOrder(double now)
        {
            if (!CanIssueOrder(now)) return false;
            OrderUsed = true;
            if (order == FlightOrder.Surge)
                Charge = Math.Min(MaxCharge, Charge + FlightPlan.SurgeCharge);
            else
                readyAt[0] = readyAt[1] = now;
            return true;
        }

        internal FlightReport Report(int milliseconds) => new()
        {
            milliseconds = milliseconds, route = route, seal = (int)seal, order = (int)order,
            escort = (int)escort, orderUsed = OrderUsed, casts = (int[])counts.Clone(), damage = (int[])dealt.Clone()
        };

        internal int Damage(int skill) => skill switch
        {
            0 => 36,
            1 => (seal == Seal.Flame ? 82 : 58) + (escort == Wing.Mechanic ? TideglassWatch.ShardPower : 0),
            _ => 156 + (escort == Wing.Observer ? TideglassWatch.FinisherPower : 0)
        };

        internal int ChargeChange(int skill) => skill switch
        {
            0 => seal == Seal.Tide ? 46 : 32,
            1 => 20,
            _ => -FinisherCost
        };

        internal double Cooldown(int skill)
        {
            if (skill == 2)
                return 5.5;
            double duration = skill == 0 ? 2.4 : 3.1;
            return seal == Seal.Gale ? duration - .7 : duration;
        }

        internal double Remaining(int skill, double now) => Math.Max(0, readyAt[skill] - now);

        internal bool CanCast(int skill, double now) =>
            !Won && Remaining(skill, now) == 0 && (skill != 2 || Charge >= FinisherCost);

        internal bool TryCast(int skill, double now, out int damage)
        {
            damage = 0;
            if (!CanCast(skill, now))
                return false;

            int power = Damage(skill);
            if (Shells > 0)
            {
                if (skill == 1) Shells--;
                else power /= 2;
            }
            if (route == 2 && skill < 2)
            {
                if (lastBasic >= 0 && lastBasic != skill)
                {
                    power += power / 2;
                    chain++;
                }
                else chain = 1;
                lastBasic = skill;
            }
            damage = Math.Min(Health, power);
            Health -= damage;
            counts[skill]++;
            dealt[skill] += damage;
            if (training && Health == 0 && !Won) Health = HealthCap;
            Charge = Math.Clamp(Charge + ChargeChange(skill), 0, MaxCharge);
            readyAt[skill] = now + Cooldown(skill);
            Casts++;
            return true;
        }
    }
}
