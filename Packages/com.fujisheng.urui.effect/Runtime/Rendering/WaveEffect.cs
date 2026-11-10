using System;
using System.Collections.Generic;
using UnityEngine;
using URUI.Rendering.Effects;
using URUI.Styles;
namespace URUI.Effect.Rendering
{
    /// <summary>原图网格变形。共享归一化网格只在细分改变时取得，时间与尺寸全部走实例属性块。</summary>
    public static class WaveEffect
    {
        static readonly StylePropertyDefinition[] parameters={
            new("--urui-wave-fixed-edge",StyleValueType.Keyword),new("--urui-wave-displacement",StyleValueType.Keyword),new("--urui-wave-amplitude",StyleValueType.Pixels),
            new("--urui-wave-frequency",StyleValueType.Number),new("--urui-wave-speed",StyleValueType.Number),new("--urui-wave-secondary",StyleValueType.Number),
            new("--urui-wave-fixed-part",StyleValueType.Number),new("--urui-wave-cross-influence",StyleValueType.Number),new("--urui-wave-cross-scale",StyleValueType.Number),
            new("--urui-wave-columns",StyleValueType.Number),new("--urui-wave-rows",StyleValueType.Number),new("--urui-wave-time-offset",StyleValueType.Number)};
        public static EffectDefinition Define(string name="ui-wave")=>new(name,EffectKind.Image,new Factory(),"wave:v1","straight texture input; premultiplied HDR output",parameters,true);
        sealed class Factory:IEffectFactory {public IElementEffect Create(EffectContext context)=>new Instance(context);}
        sealed class MaterialResource:IDisposable
        {
            internal readonly Material Material;
            internal MaterialResource(){var shader=UnityEngine.Resources.Load<Shader>("URUI.Effect/WaveImage");if(!shader||!shader.isSupported)throw new NotSupportedException("缺少 Wave Shader。");Material=new Material(shader){hideFlags=HideFlags.HideAndDontSave};}
            public void Dispose(){if(Material)URUI.Resources.GpuRetirement.DestroyOwnedObject(Material);}
        }
        sealed class Grid:IDisposable
        {
            sealed class Entry{internal Mesh mesh;internal int count;}
            static readonly Dictionary<(int,int),Entry> entries=new();
            readonly (int,int) key;Entry entry;
            internal Mesh Mesh=>entry.mesh;
            internal Grid(int columns,int rows)
            {
                key=(columns,rows);
                if(!entries.TryGetValue(key,out entry)){
                    var vertices=new Vector3[(columns+1)*(rows+1)];var uv=new Vector2[vertices.Length];var indices=new int[columns*rows*6];
                    for(int y=0;y<=rows;y++)for(int x=0;x<=columns;x++){var v=new Vector2((float)x/columns,(float)y/rows);vertices[y*(columns+1)+x]=v;uv[y*(columns+1)+x]=v;}
                    for(int y=0;y<rows;y++)for(int x=0;x<columns;x++){int v=y*(columns+1)+x,i=(y*columns+x)*6;indices[i]=v;indices[i+1]=v+columns+1;indices[i+2]=v+1;indices[i+3]=v+1;indices[i+4]=v+columns+1;indices[i+5]=v+columns+2;}
                    var mesh=new Mesh{name="URUI shared wave grid"};mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=indices;mesh.RecalculateBounds();entry=new Entry{mesh=mesh};entries.Add(key,entry);
                }entry.count++;
            }
            public void Dispose(){if(entry==null)return;var previous=entry;entry=null;if(--previous.count==0){entries.Remove(key);URUI.Resources.GpuRetirement.DestroyOwnedObject(previous.mesh);}}
        }
        sealed class Instance:IElementEffect,IEffectBounds
        {
            readonly EffectContext owner;readonly MaterialResource material;readonly MaterialPropertyBlock properties=new();Grid grid;int columns,rows;
            public Rect InkBounds{get;private set;}
            internal Instance(EffectContext owner){this.owner=owner;material=owner.Own(new MaterialResource());}
            static float Number(EffectFrame frame,string name,float minimum,float maximum){var n=frame.Style.GetNumber(name);if(n<minimum||n>maximum)throw new InvalidOperationException("Wave 参数超出范围："+name);return n;}
            public void Prepare(EffectFrame frame)
            {
                var c=Number(frame,"--urui-wave-columns",2,128);var r=Number(frame,"--urui-wave-rows",1,64);if(c!=Mathf.Floor(c)||r!=Mathf.Floor(r))throw new InvalidOperationException("Wave 细分必须为整数。");
                if(grid==null||columns!=(int)c||rows!=(int)r){var next=owner.Own(new Grid((int)c,(int)r));if(grid!=null)owner.Retire(grid);grid=next;columns=(int)c;rows=(int)r;}
                var edge=frame.Style.GetKeyword("--urui-wave-fixed-edge") switch {"left"=>0,"right"=>1,"top"=>2,"bottom"=>3,"center"=>4,_=>throw new InvalidOperationException("未知 Wave 固定边。")};
                var direction=frame.Style.GetKeyword("--urui-wave-displacement") switch {"horizontal"=>0,"vertical"=>1,_=>throw new InvalidOperationException("未知 Wave 位移方向。")};
                var amplitude=frame.Style.GetPixels("--urui-wave-amplitude");if(amplitude<0)throw new InvalidOperationException("Wave 幅度不能为负。");
                var frequency=Number(frame,"--urui-wave-frequency",0,float.MaxValue);var speed=Number(frame,"--urui-wave-speed",-float.MaxValue,float.MaxValue);var secondary=Number(frame,"--urui-wave-secondary",0,1);
                var fixedPart=Number(frame,"--urui-wave-fixed-part",0,.95f);var cross=Number(frame,"--urui-wave-cross-influence",0,1);var crossScale=Number(frame,"--urui-wave-cross-scale",-float.MaxValue,float.MaxValue);var phase=Number(frame,"--urui-wave-time-offset",-float.MaxValue,float.MaxValue);
                properties.Clear();properties.SetVector("_WaveSize",new Vector4(frame.Size.x,frame.Size.y,frame.ContentOffset.x,frame.ContentOffset.y));properties.SetVector("_WaveMotion",new Vector4(amplitude,frequency,speed,secondary));
                properties.SetVector("_WaveShape",new Vector4(fixedPart,cross,crossScale,phase));properties.SetVector("_WaveAxis",new Vector4(edge,direction,(float)frame.Time,0));properties.SetFloat("_InputBottomLeft",frame.SourceIsBottomLeft?1:0);properties.SetFloat("_InputPremultiplied",frame.SourceIsPremultiplied?1:0);
                var tint=frame.Style.GetColor("--urui-image-color");properties.SetVector("_ImageColor",new Vector4(tint.r*tint.a,tint.g*tint.a,tint.b*tint.a,tint.a));
                var verticalAxis=edge==2||edge==3||edge==4&&direction==0;var extent=amplitude*(1+secondary+cross*.5f*(verticalAxis?frame.Size.x:frame.Size.y)*Mathf.Abs(crossScale));
                InkBounds=new Rect(frame.ContentOffset-(direction==0?new Vector2(extent,0):new Vector2(0,extent)),frame.Size+(direction==0?new Vector2(2*extent,0):new Vector2(0,2*extent)));
            }
            public void Record(EffectRenderContext context){properties.SetTexture("_MainTex",context.Frame.Source);context.DrawGeometry(grid.Mesh,material.Material,Matrix4x4.identity,properties);}
            public void Dispose(){grid=null;}
        }
    }
}
