#include "rmlunity.h"
#include <RmlUi/Core.h>
#include <algorithm>
#include <cstring>
#include <deque>
#include <memory>
#include <string>
#include <unordered_map>
#include <vector>

namespace {
struct Geometry { std::vector<RuiVertex> vertices; std::vector<int> indices; };
struct Texture { int width=0,height=0; std::vector<unsigned char> pixels; };
std::string error_message;
std::deque<std::string> clicks;
class Renderer : public Rml::RenderInterface {
public:
 std::unordered_map<uint64_t, Geometry> meshes;
 std::unordered_map<uint64_t, Texture> textures;
 std::vector<RuiCommand> commands;
 uint64_t next_mesh=1,next_texture=1;
 bool clipped=false;
 Rml::Rectanglei clip={};
 Rml::CompiledGeometryHandle CompileGeometry(Rml::Span<const Rml::Vertex> vs,Rml::Span<const int> ids) override {
  uint64_t handle=next_mesh++;
  Geometry g; g.vertices.reserve(vs.size());
  for(auto& v:vs)g.vertices.push_back({v.position.x,v.position.y,v.tex_coord.x,v.tex_coord.y,v.colour.red,v.colour.green,v.colour.blue,v.colour.alpha});
  g.indices.assign(ids.begin(),ids.end()); meshes.emplace(handle,std::move(g));
  return static_cast<Rml::CompiledGeometryHandle>(handle);
 }
 void RenderGeometry(Rml::CompiledGeometryHandle geo,Rml::Vector2f move,Rml::TextureHandle tex) override {
  RuiCommand c{};c.geometry=static_cast<uint64_t>(geo);c.texture=static_cast<uint64_t>(tex);
  c.tx=move.x;c.ty=move.y;c.clip_enabled=clipped?1:0;
  if(clipped){c.clip_x=clip.Left();c.clip_y=clip.Top();c.clip_w=clip.Width();c.clip_h=clip.Height();}
  commands.push_back(c);
 }
 void ReleaseGeometry(Rml::CompiledGeometryHandle h) override {meshes.erase(static_cast<uint64_t>(h));}
 Rml::TextureHandle LoadTexture(Rml::Vector2i& dims,const Rml::String&) override {dims={0,0};return 0;}
 Rml::TextureHandle GenerateTexture(Rml::Span<const Rml::byte> pixels,Rml::Vector2i dims) override {
  if(dims.x<=0||dims.y<=0)return 0;
  uint64_t id=next_texture++;Texture t;t.width=dims.x;t.height=dims.y;
  t.pixels.assign(pixels.begin(),pixels.end());textures.emplace(id,std::move(t));
  return static_cast<Rml::TextureHandle>(id);
 }
 void ReleaseTexture(Rml::TextureHandle id) override {textures.erase(static_cast<uint64_t>(id));}
 void EnableScissorRegion(bool value) override {clipped=value;}
 void SetScissorRegion(Rml::Rectanglei value) override {clip=value;}
};
class Listener : public Rml::EventListener {
 void ProcessEvent(Rml::Event& event) override {
  if(auto* el=event.GetCurrentElement())clicks.push_back(el->GetId());
 }
};
std::unique_ptr<Renderer> render;
std::unique_ptr<Listener> listener;
Rml::Context* context=nullptr;
Rml::ElementDocument* document=nullptr;
bool started=false;
std::string safe_rml(const char* src){
 std::string out;if(!src)return out;
 for(const char* p=src;*p;++p)switch(*p){
 case '&':out+="&amp;";break;case '<':out+="&lt;";break;case '>':out+="&gt;";break;
 default:out.push_back(*p);break;
 }return out;
}
}
extern "C" {
int rui_create(int w,int h,const char* font){
 if(started)return 0;
 error_message.clear();render=std::make_unique<Renderer>();listener=std::make_unique<Listener>();
 Rml::SetRenderInterface(render.get());
 if(!Rml::Initialise()){error_message="RmlUi Initialise failed";listener.reset();render.reset();return 0;}
 started=true;
 if(font&&*font&&!Rml::LoadFontFace(font))error_message="Cannot load font file";
 context=Rml::CreateContext("rmlunity", {std::max(1,w),std::max(1,h)});
 if(!context){error_message="Cannot create RmlUi context";rui_destroy();return 0;}return 1;
}
void rui_destroy(void){
 if(document){document->Close();document=nullptr;}
 if(context){Rml::RemoveContext("rmlunity");context=nullptr;}
 if(started){Rml::Shutdown();started=false;}
 clicks.clear();listener.reset();render.reset();
}
int rui_load_document(const char* rml){
 if(!context||!rml)return 0;
 if(document){document->Close();document=nullptr;}
 document=context->LoadDocumentFromMemory(rml,"demo.rml");
 if(!document){error_message="LoadDocumentFromMemory failed";return 0;}
 for(const char* id:{"claim","inventory","shop","settings"}){
  if(auto* node=document->GetElementById(id))node->AddEventListener("click",listener.get());
 }
 document->Show();return 1;
}
void rui_resize(int w,int h){if(context)context->SetDimensions({std::max(1,w),std::max(1,h)});}
void rui_mouse_move(int x,int y){if(context)context->ProcessMouseMove(x,y,0);}
void rui_mouse_button(int b,int down){if(context){if(down)context->ProcessMouseButtonDown(b,0);else context->ProcessMouseButtonUp(b,0);}}
void rui_mouse_wheel(float d){if(context)context->ProcessMouseWheel(Rml::Vector2f(0.f,d),0);}
void rui_update(void){if(context)context->Update();}
void rui_render(void){if(!context||!render)return;render->commands.clear();render->clipped=false;context->Render();}
void rui_set_text(const char* id,const char* value){if(document&&id)if(auto* el=document->GetElementById(id))el->SetInnerRML(safe_rml(value));}
int rui_command_count(void){return render?static_cast<int>(render->commands.size()):0;}
int rui_get_commands(RuiCommand* out,int cap){if(!render||!out||cap<0)return 0;int n=std::min(cap,(int)render->commands.size());std::copy_n(render->commands.begin(),n,out);return n;}
int rui_geometry_exists(uint64_t id){return render&&render->meshes.count(id)?1:0;}
int rui_texture_exists(uint64_t id){return render&&render->textures.count(id)?1:0;}
int rui_get_geometry_info(uint64_t id,int* vc,int* ic){
 if(!render||!render->meshes.count(id))return 0;
 auto& g=render->meshes.at(id);if(vc)*vc=(int)g.vertices.size();if(ic)*ic=(int)g.indices.size();return 1;
}
int rui_copy_geometry(uint64_t id,RuiVertex* v,int vc,int* i,int ic){
 if(!render||!render->meshes.count(id)||!v||!i)return 0;auto& g=render->meshes.at(id);
 if(vc<(int)g.vertices.size()||ic<(int)g.indices.size())return 0;
 std::copy(g.vertices.begin(),g.vertices.end(),v);std::copy(g.indices.begin(),g.indices.end(),i);return 1;
}
int rui_get_texture_info(uint64_t id,int* w,int* h,int* bytes){
 if(!render||!render->textures.count(id))return 0;
 auto& t=render->textures.at(id);if(w)*w=t.width;if(h)*h=t.height;if(bytes)*bytes=(int)t.pixels.size();return 1;
}
int rui_copy_texture(uint64_t id,uint8_t* out,int cap){
 if(!render||!render->textures.count(id)||!out)return 0;auto& p=render->textures.at(id).pixels;
 if(cap<(int)p.size())return 0;std::copy(p.begin(),p.end(),out);return 1;
}
int rui_pop_event(char* out,int cap){
 if(clicks.empty()||!out)return 0;auto& value=clicks.front();if(cap<(int)value.size()+1)return -(int)value.size()-1;
 std::memcpy(out,value.c_str(),value.size()+1);int n=(int)value.size();clicks.pop_front();return n;
}
const char* rui_last_error(void){return error_message.c_str();}
}
