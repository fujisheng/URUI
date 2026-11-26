using System;
using System.Globalization;

namespace URUI.Samples.Tideglass
{
    internal enum Wing { None, Sounder, Mechanic, Observer }

    internal static class TideglassWatch
    {
        internal const int InitialCharge = 12, ShardPower = 12, FinisherPower = 28;
        internal static readonly string[] Names = { "独航", "汀岚", "赤衡", "弦昼" };
        internal static readonly string[] Roles = { "", "领航员", "轮机师", "观测官" };
        internal static readonly string[] Benefits = { "", $"起航时获得 {InitialCharge} 潮能", $"裂光伤害 +{ShardPower}", $"潮鸣伤害 +{FinisherPower}" };
        internal static readonly string[] Titles = { "见习领航", "远潮信使", "点灯人", "无隙领航员" };
        internal static readonly string[] BadgeGoals = { "首次抵达引潮站", "签收一次远航回执", "点亮第一处信标", "达标全部三项演练" };
        internal static readonly string[] Ranks = { "见习", "领航", "巡航长", "站务长" };
        internal static readonly Survey[] Routes =
        {
            new("浅湾航标测绘", "测量浅海潮差，重绘入港水道。", "sector-shoal", 35, 24, 10, Wing.Sounder),
            new("残环打捞", "带回环带漂流的光学元件。", "sector-belt", 65, 42, 16, Wing.Mechanic),
            new("暗侧听潮", "从远侧观测浮标取回星潮录音。", "sector-umbra", 95, 60, 24, Wing.Observer)
        };

        internal static string Countdown(long seconds) => TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 3600)).ToString(@"mm\:ss", CultureInfo.InvariantCulture);
        internal static long UtcNow => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    internal sealed class Survey
    {
        internal readonly string Name, Brief, Art;
        internal readonly int Seconds, Pay, Bonus;
        internal readonly Wing Specialist;
        internal Survey(string name, string brief, string art, int seconds, int pay, int bonus, Wing specialist)
        {
            Name = name; Brief = brief; Art = art;
            Seconds = seconds; Pay = pay; Bonus = bonus; Specialist = specialist;
        }
        internal int Reward(Wing officer) => Pay + (Specialist == officer ? Bonus : 0);
    }

    [Serializable]
    internal sealed class SurveyFlight
    {
        public int route, wing;
        public long returnsAt;
        internal bool Valid => (uint)route < TideglassWatch.Routes.Length && wing >= 1 && wing <= 3 && returnsAt > 0;
    }

    internal sealed partial class TideglassProfile
    {
        internal Wing Escort => (Wing)data.escort;
        internal int WornTitle => data.wornTitle;
        internal SurveyFlight Away => data.survey;
        internal int SurveyCount(int route) => data.surveys[route];
        internal int SurveyKinds => Math.Sign(SurveyCount(0)) + Math.Sign(SurveyCount(1)) + Math.Sign(SurveyCount(2));
        internal int PassedDrills => Math.Sign(DrillBest(0)) + Math.Sign(DrillBest(1)) + Math.Sign(DrillBest(2));
        internal int Merit => LitCount + PassedDrills + SurveyKinds;
        internal int Rank => Merit / 3;
        internal int LastSurveyPay => data.surveyPay;
        internal int LastSurveyRoute => data.surveyRoute;
        internal Wing LastSurveyWing => (Wing)data.surveyWing;

        void RestoreWatch()
        {
            Array.Resize(ref data.surveys, TideglassWatch.Routes.Length);
            for (int i = 0; i < data.surveys.Length; i++) data.surveys[i] = Math.Max(0, data.surveys[i]);
            if (data.survey != null && (!data.survey.Valid || !HasWing((Wing)data.survey.wing))) data.survey = null;
            if (!HasWing(Escort) || Away?.wing == data.escort) data.escort = 0;
            if (!HasTitle(WornTitle)) data.wornTitle = 0;
            if ((uint)data.surveyRoute >= TideglassWatch.Routes.Length || !HasWing(LastSurveyWing) || SurveyCount(data.surveyRoute) == 0)
                data.surveyPay = 0;
        }

        internal bool HasWing(Wing officer) => officer is Wing.Sounder or Wing.Mechanic || officer == Wing.Observer && LitCount > 0;
        internal bool Available(Wing officer) => HasWing(officer) && Away?.wing != (int)officer;
        internal bool SurveyOpen(int route) => (uint)route < TideglassWatch.Routes.Length && (route < 2 || SurveyKinds > 0);
        internal bool HasTitle(int title) => title switch { 0 => true, 1 => SurveyKinds > 0, 2 => LitCount > 0, 3 => PassedDrills == 3, _ => false };

        internal bool AssignWing(Wing officer)
        {
            if (officer != Wing.None && !Available(officer) || Escort == officer) return false;
            data.escort = (int)officer;
            persist?.Invoke(data);
            return true;
        }

        internal bool WearTitle(int title)
        {
            if (!HasTitle(title) || WornTitle == title) return false;
            data.wornTitle = title;
            persist?.Invoke(data);
            return true;
        }

        internal bool Dispatch(int route, Wing officer, long now)
        {
            if (Away != null || !SurveyOpen(route) || !Available(officer) || officer == Escort) return false;
            data.survey = new SurveyFlight { route = route, wing = (int)officer, returnsAt = now + TideglassWatch.Routes[route].Seconds };
            persist?.Invoke(data);
            return true;
        }

        internal bool Recall(long now)
        {
            if (Away == null || now >= Away.returnsAt) return false;
            data.survey = null;
            persist?.Invoke(data);
            return true;
        }

        internal bool ReceiveSurvey(long now)
        {
            var flight = Away;
            if (flight == null || now < flight.returnsAt) return false;
            data.survey = null;
            data.surveyRoute = flight.route;
            data.surveyWing = flight.wing;
            data.surveyPay = TideglassWatch.Routes[flight.route].Reward((Wing)flight.wing);
            data.surveys[flight.route] = (int)Math.Min(int.MaxValue, (long)data.surveys[flight.route] + 1);
            Credit(data.surveyPay);
            persist?.Invoke(data);
            return true;
        }
    }
}
