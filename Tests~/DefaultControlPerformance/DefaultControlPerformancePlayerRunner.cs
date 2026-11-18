using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace URUI.Validation.DefaultControlPerformance
{
    // The Player runs the exact Editor workload, including its geometry/RT assertions.
    public sealed class DefaultControlPerformancePlayerRunner : MonoBehaviour
    {
        IEnumerator Start()
        {
            if (Application.isEditor) yield break;
            string output = Argument("-urui-output");
            string kind = Argument("-urui-kind");
            Directory.CreateDirectory(output);
            Environment.SetEnvironmentVariable("URUI_DEFAULT_PERFORMANCE_OUTPUT", output);
            Environment.SetEnvironmentVariable("URUI_DEFAULT_PERFORMANCE_KIND", kind);
            var workload = new DefaultControlPerformanceTests().Measure();
            string failure = null;
            try
            {
                for (;;)
                {
                    bool next = false;
                    try { next = workload.MoveNext(); }
                    catch (Exception exception) { failure = exception.ToString(); }
                    if (failure != null || !next) break;
                    yield return workload.Current;
                }
            }
            finally { (workload as IDisposable)?.Dispose(); }
            File.WriteAllText(Path.Combine(output, "player-result.json"), JsonUtility.ToJson(new Result
            {
                passed = failure == null,
                failure = failure,
                runtime = "IL2CPP",
                development = Debug.isDebugBuild,
                unity = Application.unityVersion,
                kind = kind
            }, true));
            Application.Quit(failure == null ? 0 : 2);
        }

        static string Argument(string name)
        {
            var arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < arguments.Length; ++i)
                if (arguments[i] == name) return arguments[i + 1];
            throw new ArgumentException("Missing " + name);
        }

        [Serializable] sealed class Result
        {
            public bool passed, development;
            public string failure, runtime, unity, kind;
        }
    }
}
