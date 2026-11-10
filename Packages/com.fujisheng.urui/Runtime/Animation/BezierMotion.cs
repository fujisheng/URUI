using System;

using UnityEngine;
using URUI.Elements;

namespace URUI.Animation
{
    /// <summary>根据作者动画信号沿二次贝塞尔路径移动预声明节点；端点取自实时布局。</summary>
    public sealed class BezierMotion:IDisposable
    {
        readonly Element path,rotation,source,target;
        Vector2 start,control;
        float angle;
        bool active;
        public BezierMotion(Element path,Element rotation,Element source,Element target)
        {
            this.path=path??throw new ArgumentNullException(nameof(path));
            this.rotation=rotation??throw new ArgumentNullException(nameof(rotation));
            this.source=source??throw new ArgumentNullException(nameof(source));this.target=target??throw new ArgumentNullException(nameof(target));
            foreach(var node in new[]{rotation,source,target})
            {if(!ReferenceEquals(path.Document,node.Document)){throw new ArgumentException("路径节点必须属于同一文档。");}}
            path.Document.LayoutUpdated+=Layout;
        }
        float Number(Element element,string property)
        {
            var value=element.Document.Styles.GetComputed(element).GetNumber(property);
            return value;
        }
        float Random(string from,string to)
        {
            // 保留作者的端点顺序，使相同随机状态可复现原动画，包括反向区间。
            return UnityEngine.Random.Range(Number(path,from),Number(path,to));
        }
        /// <summary>在起终点完成布局后开始一次路径；曲线节奏继续由CSS信号决定。</summary>
        public void Begin()
        {
            if(!path.HasLayout||!source.HasLayout||!target.HasLayout){throw new InvalidOperationException("路径动画需要已布局的端点。");}
            start=target.WorldToLocal(source.LocalToWorld(source.Layout.size*.5f))-target.Layout.size*.5f;
            var direction=-start;var perpendicular=direction.sqrMagnitude>0?new Vector2(direction.y,-direction.x).normalized:Vector2.down;
            control=start*Random("--urui-motion-midpoint-from","--urui-motion-midpoint-to")+perpendicular*Random("--urui-motion-bend-from","--urui-motion-bend-to");
            angle=Random("--urui-motion-rotation-from","--urui-motion-rotation-to");active=true;Apply();
        }
        void Apply()
        {
            var t=Mathf.Clamp01(Number(path,"--urui-motion-progress"));var position=(1-t)*(1-t)*start+2*(1-t)*t*control;
            path.SetTransform(new Vector3(position.x,position.y,0),Quaternion.identity,Vector3.one);
            rotation.SetTransform(Vector3.zero,Quaternion.Euler(0,0,-angle*Mathf.Clamp01(Number(rotation,"--urui-motion-rotation-progress"))),Vector3.one);
        }
        void Layout(global::URUI.Documents.Document document){if(active&&path.IsValid&&path.HasLayout){Apply();}}
        /// <summary>结束本次路径并回到作者声明的终点。</summary>
        public void Stop()
        {
            active=false;if(path.IsValid){path.SetTransform(Vector3.zero,Quaternion.identity,Vector3.one);}
            if(rotation.IsValid){rotation.SetTransform(Vector3.zero,Quaternion.identity,Vector3.one);}
        }
        public void Dispose(){path.Document.LayoutUpdated-=Layout;Stop();}
    }
}
