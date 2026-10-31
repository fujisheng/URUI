using UnityEngine;
namespace URUI.Internal {
 // Per-recording state only. Caller supplies a verified shader dependency contract.
 internal sealed class ShaderPropertyState {
  MaterialPropertyBlock block;Texture texture;Material material;Shader shader;CommandNative previous;bool valid;
  public void Reset(){valid=false;block=null;texture=null;material=null;shader=null;}
  public bool NeedsWrite(MaterialPropertyBlock target,Texture tex,CommandNative command,bool contractAllowsReuse,Material owner){
   if(!contractAllowsReuse){Reset();return true;}
   var currentShader=owner?owner.shader:null;
   bool same=valid&&ReferenceEquals(block,target)&&ReferenceEquals(texture,tex)&&ReferenceEquals(material,owner)&&ReferenceEquals(shader,currentShader)&&previous.clip_enabled==command.clip_enabled&&previous.clip_x==command.clip_x&&previous.clip_y==command.clip_y&&previous.clip_w==command.clip_w&&previous.clip_h==command.clip_h;
   valid=true;block=target;texture=tex;material=owner;shader=currentShader;previous=command;return !same;
  }
 }
}
