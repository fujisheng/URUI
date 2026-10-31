using System;
using System.Threading;
namespace RmlUnity {
 /// <summary>Explicit weak object identity, not an ID selector. Rename never
 /// retargets this reference. Removing/replacing/unloading its object invalidates
 /// it. Dispose on the context thread; document unload releases native tokens.
 /// No finalizer calls native code. At most 65536 outstanding tokens per context.</summary>
 public sealed class RmlElementReference:IDisposable {
  readonly RmlUnityHost owner;readonly int thread;ulong token;
  internal RmlElementReference(RmlUnityHost host,ulong value){owner=host;token=value;thread=Thread.CurrentThread.ManagedThreadId;}
  void CheckThread(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("Rml element references require their context thread");}
  public bool IsValid{get{CheckThread();return token!=0&&owner.ReferenceIsValid(token);}}
  public bool SetTranslateScale2D(float x,float y,float sx,float sy){CheckThread();return token!=0&&owner.SetReferenceTransform(token,x,y,sx,sy);}
  public void Dispose(){CheckThread();if(token==0)return;owner.ReleaseReference(token);token=0;}
 }
}
