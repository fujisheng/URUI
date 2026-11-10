#include <map>
// CPU snapshots of ordered filter invocations and transformed ink bounds.
// No pointer escapes; cached native record paths explicitly decline all layer operations.
namespace {
 struct InkRegion {
  double x0=0,y0=0,x1=0,y1=0; bool valid=false;
  void Point(double x,double y) { if(!std::isfinite(x)||!std::isfinite(y)) return; if(!valid){x0=x1=x;y0=y1=y;valid=true;}else{x0=std::min(x0,x);y0=std::min(y0,y);x1=std::max(x1,x);y1=std::max(y1,y);} }
  void Join(const InkRegion& b){if(b.valid){Point(b.x0,b.y0);Point(b.x1,b.y1);}}
  void Extend(double x,double y,double halo){if(valid){auto original=*this; x0+=x-halo;y0+=y-halo;x1+=x+halo;y1+=y+halo;Join(original);}}
 };
 std::map<uint64_t,InkRegion> LayerRegions(std::map<uint64_t,std::pair<uint64_t,InkRegion>>* effect_inputs=nullptr) {
  std::map<uint64_t,InkRegion> regions;std::vector<uint64_t> stack{0};size_t next=0;
  auto operation=[&](const UruiLayerCommand& c){
   if(c.kind==1){regions[c.target];stack.push_back(c.target);}
   else if(c.kind==2){if(stack.size()>1)stack.pop_back();}
   else if(c.kind==3)regions[c.target]=regions[c.source];
   else if(c.kind==4){
    if(c.source==0){
     const auto size=context->GetDimensions();
     const int x=c.flags?c.clip_x:0,y=c.flags?c.clip_y:0;
     regions[c.target].Point(x,y);
     regions[c.target].Point(x+(c.flags?c.clip_w:size.x),y+(c.flags?c.clip_h:size.y));
    }else regions[c.target].Join(regions[c.source]);
   }
   else if(c.kind==6&&!c.blend){const auto f=render->filters.find(c.mask);if(f!=render->filters.end()){
    if(f->second.element&&effect_inputs)(*effect_inputs)[c.mask]={c.source,regions[c.source]};
    const auto& d=f->second.data; if(f->second.name=="blur")regions[c.source].Extend(0,0,std::max(0.f,d.sigma)*3);
    else if(f->second.name=="drop-shadow")regions[c.source].Extend(d.offset_x,d.offset_y,std::max(0.f,d.sigma)*3);
    else if (f->second.element && regions[c.source].valid) {
     const auto& e=f->second.expansion; auto& r=regions[c.source]; r.x0-=e[0];r.y0-=e[1];r.x1+=e[2];r.y1+=e[3];
     const auto original=r;
     for(const auto& m:f->second.coverage_transforms)for(int corner=0;corner<4;corner++) {
      const double x=corner&1?original.x1:original.x0,y=corner&2?original.y1:original.y0,w=m[3]*x+m[7]*y+m[15];
      if(std::abs(w)>1e-9)r.Point((m[0]*x+m[4]*y+m[12])/w,(m[1]*x+m[5]*y+m[13])/w);
     }
    }
   }}
  };
  for(size_t index=0;index<=render->commands.size();index++) {
   while(next<render->layer_commands.size()&&render->layer_commands[next].command_index==static_cast<int>(index))operation(render->layer_commands[next++]);
   if(index==render->commands.size()||stack.back()==0)continue;
   const auto& c=render->commands[index];auto geometry=render->meshes.find(c.geometry);if(geometry==render->meshes.end())continue;
   const auto& m=render->command_transforms[index];
   auto extra=render->effect_ink_bounds.find(static_cast<int>(index));
   if(extra!=render->effect_ink_bounds.end()) {
    const auto& b=extra->second;
    for(int corner=0;corner<4;corner++){const double x=b[0]+(corner&1?b[2]:0)+c.tx,y=b[1]+(corner&2?b[3]:0)+c.ty,w=m[3]*x+m[7]*y+m[15];
     if(std::abs(w)>1e-9)regions[stack.back()].Point((m[0]*x+m[4]*y+m[12])/w,(m[1]*x+m[5]*y+m[13])/w);
    }
   }
   for(const auto& v:geometry->second.vertices){const double x=v.x+c.tx,y=v.y+c.ty,w=m[3]*x+m[7]*y+m[15];
    if(std::abs(w)>1e-9)regions[stack.back()].Point((m[0]*x+m[4]*y+m[12])/w,(m[1]*x+m[5]*y+m[13])/w);
   }
  }
  regions.erase(0);return regions;
 }
}
extern "C" {
 int urui_effect_api_version(void){return 7;}
 int urui_element_effect_expansion(uint64_t filter,float left,float top,float right,float bottom){
  if(!render||context_thread!=std::this_thread::get_id()||urui_interaction::callback_depth||!std::isfinite(left)||!std::isfinite(top)||!std::isfinite(right)||!std::isfinite(bottom)||left<0||top<0||right<0||bottom<0)return -1;
  auto found=render->filters.find(filter);
  if(found==render->filters.end()||!found->second.element||!document_registry||!document_registry->HandleOf(found->second.element->GetOwnerDocument()))return -2;
  found->second.expansion={left,top,right,bottom};return 1;
 }
 int urui_element_effect_transforms(uint64_t filter,const float* matrices,int count) try {
  if(!render||context_thread!=std::this_thread::get_id()||urui_interaction::callback_depth||count<0||count>64||(!matrices&&count))return -1;
  auto found=render->filters.find(filter);
  if(found==render->filters.end()||!found->second.element||!document_registry||!document_registry->HandleOf(found->second.element->GetOwnerDocument()))return -2;
  for(int index=0;index<count*16;index++)if(!std::isfinite(matrices[index]))return -1;
  auto& target=found->second.coverage_transforms;target.resize(count);
  for(int index=0;index<count;index++)std::copy(matrices+index*16,matrices+(index+1)*16,target[index].begin());
  return 1;
 } catch(...) {return -8;}
 // 原图局部几何可大于RT上限；图层分配在变换后的区域计算中单独限制。
 int urui_effect_ink_bounds(int index,float x,float y,float width,float height){
  if(!render||context_thread!=std::this_thread::get_id()||index<0||size_t(index)>=render->commands.size()||!std::isfinite(x)||!std::isfinite(y)||!std::isfinite(width)||!std::isfinite(height)||width<0||height<0)return -1;
  bool image=false;for(const auto& command:render->paint_commands)if(command.command_index==index){auto found=render->paint_shaders.find(command.shader);image=found!=render->paint_shaders.end()&&found->second.kind==1;break;}
  if(!image)return -1;render->effect_ink_bounds[index]={x,y,width,height};return 1;
 }
 int urui_element_visible_in_hierarchy(UruiDocumentHandle document_handle, uint64_t token){
        auto* document = urui_elements::Document(document_handle);auto* e=urui_elements::Get(document_handle, token);return e&&e->GetOwnerDocument()==document&&e->IsVisible(true)?1:0;}
 int urui_filter_api_version(void){return 3;}
 UruiDocumentHandle urui_filter_document(uint64_t handle){
  if(!render||context_thread!=std::this_thread::get_id())return 0;
  const auto found=render->filters.find(handle);return found==render->filters.end()?0:found->second.document;
 }
 int urui_filter_info(uint64_t handle,UruiFilter* result,char* name,int capacity){
  if(!render||!result||capacity<0||(!name&&capacity)||context_thread!=std::this_thread::get_id())return -1;
  auto found=render->filters.find(handle);if(found==render->filters.end())return -1;
  const int required=static_cast<int>(found->second.name.size())+1;
  if(capacity<required)return -required;
  *result=found->second.data;
  if (found->second.element) {
   auto* element=found->second.element.get();
   result->element=element_references.Track(element,element->GetOwnerDocument());
   if (!result->element) return -1;
  }
  std::memcpy(name,found->second.name.c_str(),required);return required;
 }
 int urui_get_layer_regions(UruiLayerRegion* out,int capacity){
  if(!render||capacity<0||(!out&&capacity)||context_thread!=std::this_thread::get_id())return -1;
  const auto regions=LayerRegions();if(!out)return static_cast<int>(regions.size());if(capacity<static_cast<int>(regions.size()))return -2;
  int count=0;for(const auto& pair:regions){const auto& b=pair.second;
   const double left=std::floor(b.x0),top=std::floor(b.y0),right=std::ceil(b.x1),bottom=std::ceil(b.y1);
   if(!b.valid){out[count++]={pair.first,0,0,1,1};continue;}
   if(right-left>8192||bottom-top>8192||std::abs(left)>1000000||std::abs(top)>1000000)return -3;
   out[count++]={pair.first,int(left),int(top),std::max(1,int(right-left)),std::max(1,int(bottom-top))};
  }return count;
 }
 int urui_get_effect_input_regions(UruiEffectInputRegion* out,int capacity){
  if(!render||capacity<0||(!out&&capacity)||context_thread!=std::this_thread::get_id())return -1;
  std::map<uint64_t,std::pair<uint64_t,InkRegion>> inputs;LayerRegions(&inputs);
  if(!out)return static_cast<int>(inputs.size());if(capacity<static_cast<int>(inputs.size()))return -2;
  int count=0;for(const auto& entry:inputs) {
   const auto& b=entry.second.second;const double left=std::floor(b.x0),top=std::floor(b.y0),right=std::ceil(b.x1),bottom=std::ceil(b.y1);
   if(!b.valid){out[count++]={entry.first,entry.second.first,0,0,1,1};continue;}
   if(right-left>8192||bottom-top>8192||std::abs(left)>1000000||std::abs(top)>1000000)return -3;
   out[count++]={entry.first,entry.second.first,int(left),int(top),std::max(1,int(right-left)),std::max(1,int(bottom-top))};
  }return count;
 }
}
