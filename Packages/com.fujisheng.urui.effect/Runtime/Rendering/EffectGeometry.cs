using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using URUI.Rendering.Effects;

namespace URUI.Effect.Rendering
{
    /// <summary>源三角流上的 Flip、阴影与镜像；只在几何参数改变时重建。</summary>
    internal sealed class EffectGeometry
    {
        struct Vertex
        {
            internal Vector3 position;
            internal Vector2 uv, flags;
            internal Color32 color;
        }
        sealed class Lease : IDisposable
        {
            internal readonly Mesh Mesh;
            internal Lease(Mesh mesh) { Mesh=mesh; }
            public void Dispose() { if(Mesh)EffectObjectLifetime.Destroy(Mesh); }
        }
        static readonly Vector2[][] vectors = {
            Array.Empty<Vector2>(),new[]{Vector2.one},new[]{Vector2.one,Vector2.right,Vector2.up},
            new[]{Vector2.one,-Vector2.one,new Vector2(1,-1),new Vector2(-1,1)},
            new[]{Vector2.one,-Vector2.one,new Vector2(1,-1),new Vector2(-1,1),Vector2.right,Vector2.up,Vector2.left,Vector2.down}
        };
        readonly EffectContext context;
        Lease geometry;
        (ulong,Mesh,Vector2,Rect,Rect,Rect,Matrix4x4,ShadowMode,Vector2,int,float,float,Flip) key;
        internal Mesh Mesh=>geometry?.Mesh;
        internal Rect InkBounds {get;private set;}
        internal EffectGeometry(EffectContext context) {this.context=context;}
        internal static Matrix4x4[] BoundsTransforms(EffectLayout layout)
        {
            var style=layout.Style;
            var flags=(Flip)Mathf.Clamp(Mathf.RoundToInt(style.GetNumber("--urui-fx-flip")),0,15);
            var mode=EffectShadowProperties.ReadMode(style.GetKeyword("--urui-fx-shadow-mode"));
            var distance=new Vector2(Mathf.Clamp(style.GetNumber("--urui-fx-shadow-distance-x"),-600,600),Mathf.Clamp(style.GetNumber("--urui-fx-shadow-distance-y"),-600,600));
            if((flags&Flip.Shadow)!=0) {if((flags&Flip.Horizontal)!=0)distance.x=-distance.x;if((flags&Flip.Vertical)!=0)distance.y=-distance.y;}
            var flip=(flags&(Flip.Horizontal|Flip.Vertical))!=0;
            var shadow=mode!=ShadowMode.None&&distance!=Vector2.zero;
            if(!flip&&!shadow)return Array.Empty<Matrix4x4>();
            var iterations=Mathf.Clamp(Mathf.RoundToInt(style.GetNumber("--urui-fx-shadow-iteration")),1,5);
            var count=(flip?1:0)+(shadow ? mode==ShadowMode.Mirror ? 1 : vectors[(int)mode].Length*iterations : 0);
            var result=new Matrix4x4[count];var index=0;var reflection=Matrix4x4.identity;
            var center=layout.ContentRect.center;
            if(flip)reflection=Matrix4x4.Translate(center)*Matrix4x4.Scale(new Vector3((flags&Flip.Horizontal)!=0?-1:1,(flags&Flip.Vertical)!=0?-1:1,1))*Matrix4x4.Translate(-center);
            var matrix=layout.ElementToDocument;var inverse=matrix.inverse;
            if(flip)result[index++]=matrix*reflection*inverse;
            if(shadow&&mode==ShadowMode.Mirror)
            {
                var scale=Mathf.Clamp(style.GetNumber("--urui-fx-shadow-mirror-scale"),0,2);
                var mirror=Matrix4x4.identity;mirror[1,1]=-scale;
                mirror[1,3]=2*layout.ContentRect.y+(layout.ContentRect.height+layout.RootContentRect.height)*.5f*(1+scale)-distance.y;
                result[index]=matrix*mirror*reflection*inverse;
            }
            else if(shadow)
            {
                var offset=Vector2.zero;
                for(var iteration=0;iteration<iterations;iteration++)
                {
                    offset+=distance/(iteration+1);
                    foreach(var direction in vectors[(int)mode])
                        result[index++]=matrix*Matrix4x4.Translate(new Vector3(offset.x*direction.x,-offset.y*direction.y,0))*reflection*inverse;
                }
            }
            return result;
        }
        internal static Vector2 Distance(EffectParameters p)
        {
            var value=p.ShadowDistance;
            if((p.Flip&Flip.Shadow)!=0)
            { if((p.Flip&Flip.Horizontal)!=0)value.x=-value.x;if((p.Flip&Flip.Vertical)!=0)value.y=-value.y; }
            return new Vector2(Mathf.Clamp(value.x,-600,600),Mathf.Clamp(value.y,-600,600));
        }
        internal void Prepare(EffectFrame frame,EffectParameters p,Mesh sampled)
        {
            var distance=Distance(p);var shadow=p.ShadowMode!=ShadowMode.None && distance!=Vector2.zero;
            var flip=(p.Flip&(Flip.Horizontal|Flip.Vertical))!=0;
            var sampling=frame.SourceIsCaptured&&EffectSampling.GeometryExpansion(p)!=Vector4.zero;
            if(!shadow && !flip && !sampling)
            {
                if(geometry!=null)context.Retire(geometry);geometry=null;
                InkBounds=frame.SourceIsCaptured ? new Rect(frame.ContentOffset,frame.Size) : frame.SourceGeometry.Bounds;return;
            }
            var nextKey=(frame.SourceGeometry.GeometryId,sampled,frame.Size,frame.SourceCaptureBounds,frame.SourceUvBounds,frame.Layout.RootContentRect,frame.ElementToDocument,
                p.ShadowMode,distance,p.ShadowIteration,p.ShadowFade,p.ShadowMirrorScale,p.Flip);
            if(geometry!=null && key.Equals(nextKey))return;
            var vertices=Read(frame,p,sampled);
            var center=frame.ContentOffset+frame.Size*.5f;
            if(flip)for(var i=0;i<vertices.Count;i++)
            {
                var vertex=vertices[i];
                if((p.Flip&Flip.Horizontal)!=0)vertex.position.x=2*center.x-vertex.position.x;
                if((p.Flip&Flip.Vertical)!=0)vertex.position.y=2*center.y-vertex.position.y;
                vertices[i]=vertex;
            }
            if(shadow)
            {
                if(p.ShadowMode==ShadowMode.Mirror)Mirror(vertices,frame,p,distance);
                else Shadow(vertices,vectors[(int)p.ShadowMode],distance,p.ShadowIteration,p.ShadowFade);
            }
            var low=new Vector2(float.MaxValue,float.MaxValue);var high=new Vector2(float.MinValue,float.MinValue);
            var positions=new Vector3[vertices.Count];var uv=new Vector2[vertices.Count];var flags=new Vector2[vertices.Count];
            var colors=new Color32[vertices.Count];var indices=new int[vertices.Count];
            var toUnit=Matrix4x4.Scale(new Vector3(1/Mathf.Max(1,frame.SourceCaptureBounds.width),1/Mathf.Max(1,frame.SourceCaptureBounds.height),1))*
                Matrix4x4.Translate(new Vector3(-frame.SourceCaptureBounds.x,-frame.SourceCaptureBounds.y,0))*frame.ElementToDocument;
            for(var i=0;i<vertices.Count;i++)
            {
                var vertex=vertices[i];low=Vector2.Min(low,vertex.position);high=Vector2.Max(high,vertex.position);
                positions[i]=frame.SourceIsCaptured ? toUnit.MultiplyPoint(vertex.position) : vertex.position;
                uv[i]=vertex.uv;flags[i]=vertex.flags;colors[i]=vertex.color;indices[i]=i;
            }
            InkBounds=Rect.MinMaxRect(low.x,low.y,high.x,high.y);
            var mesh=new Mesh {name="URUI Effect Geometry",indexFormat=vertices.Count>65535 ? IndexFormat.UInt32 : IndexFormat.UInt16};
            try
            {
                mesh.vertices=positions;mesh.uv=uv;mesh.uv2=flags;mesh.colors32=colors;mesh.triangles=indices;mesh.RecalculateBounds();
                var next=context.Own(new Lease(mesh));if(geometry!=null)context.Retire(geometry);geometry=next;key=nextKey;
            }
            catch {EffectObjectLifetime.Destroy(mesh);throw;}
        }
        static List<Vertex> Read(EffectFrame frame,EffectParameters parameters,Mesh sampled)
        {
            if(frame.SourceIsCaptured)
            {
                // RT 是保守的整数覆盖范围；实际输出网格仍遵守源算法的浮点扩边，不能多画 padding 的最后一行。
                var capture=frame.SourceCaptureBounds;var domain=frame.SourceUvBounds;
                var ink=new Rect(capture.x+domain.x*capture.width,capture.y+(1-domain.yMax)*capture.height,
                    domain.width*capture.width,domain.height*capture.height);
                var expansion=EffectSampling.GeometryExpansion(parameters);
                var scaleX=ink.width*.5f/Mathf.Max(.00001f,ink.width-.002f);
                var scaleY=ink.height*.5f/Mathf.Max(.00001f,ink.height-.002f);
                var bounds=Rect.MinMaxRect(ink.xMin-expansion.x*scaleX,ink.yMin-expansion.y*scaleY,ink.xMax+expansion.z*scaleX,ink.yMax+expansion.w*scaleY);
                var world=new[]{new Vector3(bounds.xMin,bounds.yMin),new Vector3(bounds.xMax,bounds.yMin),
                    new Vector3(bounds.xMax,bounds.yMax),new Vector3(bounds.xMin,bounds.yMax)};
                var order=new[]{0,1,2,0,2,3};var result=new List<Vertex>(6);var inverse=frame.ElementToDocument.inverse;
                foreach(var index in order)
                {
                    var point=world[index];result.Add(new Vertex {position=inverse.MultiplyPoint(point),
                        uv=new Vector2((point.x-capture.x)/capture.width,1-(point.y-capture.y)/capture.height),color=Color.white});
                }
                return result;
            }
            var source=frame.SourceGeometry;
            var positions=sampled ? sampled.vertices : null;var uv=sampled ? sampled.uv : null;
            var colors=sampled ? sampled.colors32 : null;var indices=sampled ? sampled.triangles : null;
            var count=sampled ? indices.Length : source.Indices.Count;
            var vertices=new List<Vertex>(count);
            for(var i=0;i<count;i++)
            {
                var id=sampled ? indices[i] : source.Indices[i];
                var position=sampled ? positions[id] : source.Positions[id];
                if(frame.SourceIsCaptured)
                {
                    var world=new Vector3(frame.SourceCaptureBounds.x+position.x*frame.SourceCaptureBounds.width,
                        frame.SourceCaptureBounds.y+position.y*frame.SourceCaptureBounds.height,0);
                    position=frame.ElementToDocument.inverse.MultiplyPoint(world);
                }
                vertices.Add(new Vertex {position=position,uv=sampled ? uv[id] : source.UVs[id],color=sampled ? colors[id] : source.Colors[id]});
            }
            return vertices;
        }
        static Color32 Fade(Color32 color,float alpha)
        {
            var next=(byte)(Mathf.Clamp01(alpha)*color.a);
            if(color.a==0)return new Color32(0,0,0,0);
            return new Color32((byte)(color.r*next/color.a),(byte)(color.g*next/color.a),(byte)(color.b*next/color.a),next);
        }
        static void Shadow(List<Vertex> vertices,Vector2[] directions,Vector2 distance,int iterations,float fade)
        {
            var count=vertices.Count;var start=0;var end=count;var offset=Vector2.zero;var alpha=fade;
            for(var iteration=0;iteration<iterations;iteration++)
            {
                offset+=distance/(iteration+1);
                foreach(var direction in directions)
                {
                    for(var i=0;i<count;i++)
                    {
                        vertices.Add(vertices[end-count+i]);var vertex=vertices[start+i];
                        vertex.position+=new Vector3(offset.x*direction.x,-offset.y*direction.y,0);
                        vertex.color=Fade(vertex.color,alpha);vertices[start+i]=vertex;
                    }
                    start=end;end=vertices.Count;
                }
                alpha*=.75f;
            }
            for(var i=0;i<vertices.Count-count;i++) {var vertex=vertices[i];vertex.flags=Vector2.right;vertices[i]=vertex;}
        }
        static Vertex Interpolate(Vertex a,Vertex b,float amount)
        {
            return new Vertex {position=Vector3.Lerp(a.position,b.position,amount),uv=Vector2.Lerp(a.uv,b.uv,amount),
                color=Color.Lerp(a.color,b.color,amount),flags=Vector2.up};
        }
        static void Mirror(List<Vertex> vertices,EffectFrame frame,EffectParameters p,Vector2 distance)
        {
            var original=vertices.ToArray();var reflected=new List<Vertex>(original.Length);
            var height=frame.Layout.RootContentRect.height;var top=frame.ContentOffset.y;var rate=Mathf.Clamp01(distance.x);
            var halfSum=(frame.Size.y+height)*.5f;
            var threshold=top+halfSum-height*rate;
            var origin=2*top+halfSum*(1+p.ShadowMirrorScale)-distance.y;
            for(var start=0;start<original.Length;start+=3)
            {
                var polygon=new List<Vertex>(4);
                for(var corner=0;corner<3;corner++)
                {
                    var vertex=original[start+corner];
                    var normalized=(halfSum-(vertex.position.y-top))/Mathf.Max(.001f,height);
                    // 源 Mirror 只选择镜像坐标矩阵，仍走主体颜色/采样，而不是 ShadowColor 分支。
                    vertex.color=Fade(Fade(vertex.color,p.ShadowFade),Mathf.InverseLerp(rate,0,normalized));vertex.flags=Vector2.up;
                    polygon.Add(vertex);
                }
                var clipped=new List<Vertex>(4);
                for(var i=0;i<polygon.Count;i++)
                {
                    var a=polygon[i];var b=polygon[(i+1)%polygon.Count];var inside=a.position.y>=threshold;var nextInside=b.position.y>=threshold;
                    if(inside)clipped.Add(a);
                    if(inside!=nextInside)clipped.Add(Interpolate(a,b,(threshold-a.position.y)/(b.position.y-a.position.y)));
                }
                for(var i=1;i+1<clipped.Count;i++) {reflected.Add(clipped[0]);reflected.Add(clipped[i]);reflected.Add(clipped[i+1]);}
            }
            for(var i=0;i<reflected.Count;i++)
            {var vertex=reflected[i];vertex.position.y=origin-p.ShadowMirrorScale*vertex.position.y;reflected[i]=vertex;}
            vertices.Clear();vertices.AddRange(reflected);vertices.AddRange(original);
        }
        internal static void BindMirror(MaterialPropertyBlock properties,EffectFrame frame,EffectParameters p)
        {
            var distance=Distance(p);var inverse=Matrix4x4.identity;
            if(p.ShadowMode==ShadowMode.Mirror && p.ShadowMirrorScale>0)
            {
                inverse[1,1]=-1/p.ShadowMirrorScale;
                inverse[1,3]=(2*frame.Layout.RootContentRect.y+frame.Layout.RootContentRect.height*(1+p.ShadowMirrorScale)-distance.y)/p.ShadowMirrorScale;
            }
            var warp=frame.Layout.RootToDocument*inverse*frame.Layout.RootToDocument.inverse;
            properties.SetMatrix("_EffectMirrorRootMatrix",properties.GetMatrix("_EffectRootMatrix")*warp);
            properties.SetMatrix("_EffectMirrorGradMatrix",properties.GetMatrix("_EffectGradMatrix")*warp);
        }
    }
}
