using System;
using System.Globalization;
using UnityEngine;

namespace URUI.Samples.Tideglass
{
    internal sealed class TideglassRoute
    {
        internal readonly string Name, Target, Hint, Art;
        internal readonly int Hull, Pay;

        TideglassRoute(string name, string target, string hint, string art, int hull, int pay)
        {
            Name = name;
            Target = target;
            Hint = hint;
            Art = art;
            Hull = hull;
            Pay = pay;
        }

        internal static readonly TideglassRoute[] All =
        {
            new("静潮浅湾", "偏振核心", "引潮积蓄潮能。达到 60 点后，\n以潮鸣击穿核心。", "sector-shoal", 480, 60),
            new("碎镜星环", "镜壳卫星", "镜壳会吸收半数伤害。\n用裂光依次剥除两层镜壳。", "sector-belt", 620, 90),
            new("长夜食海", "蚀相之核", "交替施放引潮与裂光，\n后一击伤害提高一半。\n潮鸣保留连携。", "sector-umbra", 760, 140)
        };

        internal static string TimeLabel(int milliseconds) =>
            milliseconds > 0 ? (milliseconds / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " 秒" : "—";
    }

    [Serializable]
    internal sealed class VoyageSave
    {
        public int version = 1;
        public int balance = 180;
        public int equipped;
        public int ownedMask = 1;
        public int claimedMask;
        public int activePreset;
        public int[] presetSeals;
        public int[] presetOrders;
        public int[] drillBest;
        public FlightReport lastFlight;
        public int escort, wornTitle;
        public int[] surveys;
        public SurveyFlight survey;
        public int surveyPay, surveyRoute, surveyWing;
        public bool calibrated;
        public int[] clears = new int[3];
        public int[] bestMs = new int[3];
    }

    internal static class TideglassSave
    {
        internal const string Key = "URUI.Tideglass.Voyage.v1";

        internal static TideglassProfile Load()
        {
            VoyageSave save = null;
            if (PlayerPrefs.HasKey(Key))
            {
                try
                {
                    save = JsonUtility.FromJson<VoyageSave>(PlayerPrefs.GetString(Key));
                }
                catch (ArgumentException) { /* An unreadable local profile starts a fresh flight. */ }
            }
            return new TideglassProfile(save, Write);
        }

        static void Write(VoyageSave data)
        {
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }
    }
}
