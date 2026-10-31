#if UNITY_EDITOR
using System;
using System.Linq;

public static class ManagedApiAvailability
{
    public static void Check()
    {
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("URUI.Document"))
            .FirstOrDefault(candidate => candidate != null);
        if (type == null)
        {
            throw new Exception("URUI_MANAGED_API_MISSING: expected URUI.Document component.");
        }
    }
}

#endif
