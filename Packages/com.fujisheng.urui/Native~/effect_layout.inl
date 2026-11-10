#include <URUI/Core/ElementUtilities.h>

namespace {
 void CopyEffectLayout(URUI::Element* element,float* output) {
  const auto& box=element->GetBox();
  const auto position=box.GetPosition(URUI::BoxArea::Content)-box.GetPosition(URUI::BoxArea::Border);
  const auto size=box.GetSize(URUI::BoxArea::Content);
  output[0]=position.x;output[1]=position.y;output[2]=size.x;output[3]=size.y;
  const auto offset=element->GetAbsoluteOffset(URUI::BoxArea::Border);
  auto matrix=URUI::Matrix4f::Translate(offset.x,offset.y,0);
  if (const auto* state=element->GetTransformState()) if (const auto* transform=state->GetTransform()) matrix=*transform*matrix;
  const auto* m=matrix.data();
  // z=0 平面到文档的完整单应矩阵，保留透视 w，不能用三个投影点近似为仿射变换。
  const float homography[16]={m[0],m[1],0,m[3],m[4],m[5],0,m[7],0,0,1,0,m[12],m[13],0,m[15]};
  std::copy(homography,homography+16,output+4);
 }
}
extern "C" {
 int urui_element_effect_state(UruiDocumentHandle owner,uint64_t token,int capture_required,int input_disabled) try {
  if(!render||context_thread!=std::this_thread::get_id()||urui_interaction::callback_depth||capture_required<0||capture_required>1||input_disabled<0||input_disabled>1)return -1;
  auto* element=urui_elements::Get(owner,token);if(!element)return -2;
  element->effect_capture_required=capture_required!=0;element->effect_input_disabled=input_disabled!=0;return 1;
 } catch(...) {return -8;}
 int urui_document_effect_image_condition(UruiDocumentHandle owner,const char* effect,const char* property,const char* direct_value) try {
  URUI::Bridge::DocumentState* state=nullptr;
  const auto status=urui_documents::Find(owner,state,false,true);if(status!=1)return status;
  if(state->Element()||!effect||!*effect||!property||property[0]!='-'||property[1]!='-'||!direct_value||!*direct_value)return -1;
  urui_resources::Pending(*state)->RegisterEffectImageCondition(effect,URUI::EffectImageCondition{property,direct_value});return 1;
 } catch(const std::invalid_argument&) {return -1;} catch(...) {return -8;}
 int urui_effect_layout(UruiDocumentHandle owner,uint64_t token,float* output,int capacity,uint64_t* root) try {
  if(!render||!output||capacity<40||!root||context_thread!=std::this_thread::get_id()||urui_interaction::callback_depth)return -1;
  auto* document=urui_elements::Document(owner);auto* element=urui_elements::Get(owner,token);
  if(!document||!element||element->GetOwnerDocument()!=document||!render->EffectAttached(element))return -2;
  static const URUI::String root_attribute="data-urui-effect-root";
  const auto* attribute=element->GetAttribute(root_attribute);
  static const URUI::String empty;
  const auto& name=attribute&&attribute->GetType()==URUI::Variant::STRING ? attribute->GetReference<URUI::String>() : empty;
  auto& reference=render->effect_roots[token];
  const auto structure=document->GetEffectStructureRevision();
  if(reference.element.get()!=element||reference.name!=name||reference.structure!=structure||!reference.root) {
   auto* target=name.empty() ? element : URUI::ElementUtilities::GetElementById(document,name);
   if(!target||target->GetOwnerDocument()!=document||!render->EffectAttached(target))target=element;
   reference={element->GetObserverPtr(),target->GetObserverPtr(),name,structure};
  }
  auto* target=reference.root.get();
  *root=element_references.Track(target,document);if(!*root)return -2;
  CopyEffectLayout(element,output);CopyEffectLayout(target,output+20);return 1;
 } catch(...) {return -8;}
}
