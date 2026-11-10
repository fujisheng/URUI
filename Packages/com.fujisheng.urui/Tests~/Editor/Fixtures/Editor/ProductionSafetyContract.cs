#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

namespace URUI.Tests.Editor
{
    /// <summary>
    /// 检查正式配置仍能拒绝无法证明 GPU 已完成的提交；不分配或伪造实际 GPU 资源。
    /// </summary>
    public static class ProductionSafetyContract
    {
        public static void Check(Assembly runtime)
        {
            var hostType=runtime.GetType("URUI.Internal.Hosting.RenderHost",true);
            foreach(var field in hostType.GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly))
                if(field.FieldType==typeof(bool))throw new Exception("Runtime performance switch remains: "+field.Name);
            var host=FormatterServices.GetUninitializedObject(hostType);
            hostType.GetField("hasOwnedImages",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(host,true);
            var rejected=false;
            try{hostType.GetProperty("RenderBuffer",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(host);}
            catch(TargetInvocationException error){rejected=error.InnerException is InvalidOperationException;}
            if(!rejected)throw new Exception("Owned GPU resources must reject untracked command-buffer borrowing.");

            Debug.Log("URUI_PRODUCTION_FENCE_GUARD_PASS");
        }
    }
}
#endif
