using UnityEngine;

namespace RmlUnity {
 // Shared timing and curves. Both UI implementations render these same phases.
 public static class Match3Motion {
  public const float SwapSeconds=0.24f,ClearSeconds=0.27f,FallSeconds=0.34f;
  public static float EaseInOut(float t){t=Mathf.Clamp01(t);return t*t*(3f-2f*t);}
  public static float EaseOutCubic(float t){t=Mathf.Clamp01(t);float a=1f-t;return 1f-a*a*a;}
  public static float EaseOutBack(float t){t=Mathf.Clamp01(t);float a=t-1f;return 1f+2.70158f*a*a*a+1.70158f*a*a;}
  public static float FallProgress(float elapsed,int column){return EaseOutBack((elapsed-column*0.012f)/(FallSeconds-column*0.012f));}
 }
}
