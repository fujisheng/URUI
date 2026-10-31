using UnityEngine;
namespace RmlUnity.Samples {
 // Add this component to an empty GameObject; no uGUI or benchmark package required.
 public sealed class HelloRml : MonoBehaviour {
  RmlUnityHost host;TextAsset markup;
  void Start() {
   if(!Camera.main) { var camera=new GameObject("Main Camera").AddComponent<Camera>();camera.tag="MainCamera";camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black; }
   host=gameObject.AddComponent<RmlUnityHost>();
   markup=new TextAsset("<rml><head><style>body{font-family:__RMLUNITY_FONT__;color:white;font-size:28px;}#panel{width:300px;height:100px;background-color:#204080;}</style></head><body><div id='panel'><span id='label'>Hello RmlUnity</span></div></body></rml>");host.document=markup;
  }
  bool updated;
  void Update() { if(!updated&&host&&host.Ready) {host.SetText("label","Managed API, native layout");host.SetTranslateScale2D("panel",12,12,1,1);updated=true;} }
  void OnDestroy(){if(markup)Destroy(markup);}
 }
}
