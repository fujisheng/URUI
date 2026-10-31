#pragma once
// Diagnostic-only hooks. Never cache an Element pointer; active exists only
// during the synchronous urui_set_style call on the calling thread.
#include "urui.h"
#include <RmlUi/Core/Transform.h>
#include <chrono>
namespace urui_style_probe {
extern thread_local UruiStyleProbe total;
extern thread_local int mode;
extern thread_local UruiStyleProbe* active;
struct Scope {
 uint64_t* out;std::chrono::steady_clock::time_point start;
 explicit Scope(uint64_t* p):out(p){if(out)start=std::chrono::steady_clock::now();}
 ~Scope(){if(out)*out+=std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now()-start).count();}
};
// Exact comparison of the existing inline declaration, not computed style.
// Complex unknown representations are counted unsupported, never equal.
inline bool Supported(const Rml::Property& a){
 if(a.unit==Rml::Unit::TRANSFORM){auto t=a.Get<Rml::TransformPtr>();if(!t)return true;
  for(int i=0;i<t->GetNumPrimitives();++i){auto k=t->GetPrimitive(i).type;
   if(k!=Rml::TransformPrimitive::TRANSLATE2D&&k!=Rml::TransformPrimitive::SCALE2D&&k!=Rml::TransformPrimitive::MATRIX2D)return false;
  }return true;
 }
 switch(a.value.GetType()){
  case Rml::Variant::FLOAT:case Rml::Variant::INT:case Rml::Variant::STRING:
  case Rml::Variant::COLOURB:case Rml::Variant::COLOURF:return true;
  default:return false;
 }
}
inline bool Equal(const Rml::Property& a,const Rml::Property& b,bool& supported){
 supported=Supported(a)&&Supported(b);if(!supported)return false;
 if(a.unit!=b.unit)return false;
 if(a.value.GetType()!=b.value.GetType())return false;
 if(a.unit!=Rml::Unit::TRANSFORM){
  switch(a.value.GetType()){
   case Rml::Variant::FLOAT:case Rml::Variant::INT:case Rml::Variant::STRING:
   case Rml::Variant::COLOURB:case Rml::Variant::COLOURF:return a==b;
   default:supported=false;return false;
  }
 }
 auto x=a.Get<Rml::TransformPtr>(),y=b.Get<Rml::TransformPtr>();
 if(!x||!y)return x==y;
 if(x->GetNumPrimitives()!=y->GetNumPrimitives())return false;
 bool equal=true;
 for(int i=0;i<x->GetNumPrimitives();++i){auto& p=x->GetPrimitive(i);auto& q=y->GetPrimitive(i);
  if(p.type!=q.type)return false;
  switch(p.type){
   case Rml::TransformPrimitive::TRANSLATE2D:equal&=p.translate_2d.values==q.translate_2d.values;break;
   case Rml::TransformPrimitive::SCALE2D:equal&=p.scale_2d.values==q.scale_2d.values;break;
   case Rml::TransformPrimitive::MATRIX2D:equal&=p.matrix_2d.values==q.matrix_2d.values;break;
   default:supported=false;return false;
  }
 }
 return equal;
}
}
