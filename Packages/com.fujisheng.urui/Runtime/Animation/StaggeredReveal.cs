using System;
using System.Collections.Generic;

using URUI.Documents;
using URUI.Elements;
using UnityEngine;

namespace URUI.Animation
{
    /// <summary>由作者状态 class 激活的逐项进场，布局与关键帧均来自外置资源。</summary>
    public sealed class StaggeredReveal : IDisposable
    {
        static readonly URUI.Styles.StylePropertyDefinition delayProperty=new("--urui-stagger-delay",URUI.Styles.StyleValueType.Number);
        static readonly URUI.Styles.StylePropertyDefinition boundaryProperty=new("--urui-stagger-boundary",URUI.Styles.StyleValueType.Keyword);
        sealed class Entry
        {
            internal Element element;
            internal string previousDistance;
            internal bool previousClass;
        }
        readonly Element root,container;
        readonly Document document;
        readonly string activeClass,prepareClass,revealedClass;
        readonly List<Entry> entries=new();
        double started,gap,delay;
        int revealed;
        bool prepared,activated,disposed;

        /// <summary>容器用 --urui-stagger-edge 和以秒为数值的 --urui-stagger-span/--urui-stagger-gap 声明时序。</summary>
        /// <remarks>关键帧读取动态 --urui-stagger-distance；控制器只发布实测距离和 class，不创建节点或样式文本。</remarks>
        public StaggeredReveal(Element root,Element container,string activeClass,string prepareClass,string revealedClass)
        {
            if(root==null||container==null){throw new ArgumentNullException(root==null?nameof(root):nameof(container));}
            if(!ReferenceEquals(root.Document,container.Document)){throw new ArgumentException("进场根与容器必须属于同一文档。");}
            if(string.IsNullOrWhiteSpace(activeClass)||string.IsNullOrWhiteSpace(prepareClass)||string.IsNullOrWhiteSpace(revealedClass))
            {throw new ArgumentException("进场状态 class 不能为空。");}
            this.root=root;this.container=container;this.activeClass=activeClass;this.prepareClass=prepareClass;this.revealedClass=revealedClass;
            document=root.Document;document.LayoutUpdated+=Update;document.Unloaded+=Unloaded;
        }
        void Update(Document owner)
        {
            if(disposed||!root.IsValid||!root.HasLayout||!container.IsValid||!container.HasLayout){return;}
            var preparing=root.ClassListContains(prepareClass);var active=root.ClassListContains(activeClass);
            if(preparing)
            {
                if(!prepared){Prepare();}
                return;
            }
            if(!active){if(prepared){Clear();}return;}
            if(!prepared){Prepare();}
            if(!activated){started=document.Surface.UiClock.Now;activated=true;}
            var elapsed=document.Surface.UiClock.Now-started-delay;
            while(revealed<entries.Count&&elapsed>=gap*revealed)
            {
                var entry=entries[revealed++];
                if(entry.element.IsValid){entry.element.AddToClassList(revealedClass);}
            }
        }
        void Prepare()
        {
            var span=Number("--urui-stagger-span");var maximumGap=Number("--urui-stagger-gap");
            delay=document.Styles.GetComputed(container).Contains(delayProperty)?Number("--urui-stagger-delay"):0;
            var edge=document.Styles.GetComputed(container).GetKeyword("--urui-stagger-edge");
            if(edge is not ("left" or "right" or "top" or "bottom")){throw new InvalidOperationException("进场边缘必须为 left/right/top/bottom。");}
            var bounds=new Rect(Vector2.zero,container.Layout.size);
            var snapshot=document.Styles.GetComputed(container);var boundary=snapshot.Contains(boundaryProperty)?snapshot.GetKeyword("--urui-stagger-boundary"):null;
            if(boundary=="parent")
            {
                var parent=container.Parent;
                if(parent==null||!parent.HasLayout){throw new InvalidOperationException("父视口进场需要已布局的父节点。");}
                var rect=parent.ContentRect;
                var a=container.WorldToLocal(parent.LocalToWorld(rect.min));
                var b=container.WorldToLocal(parent.LocalToWorld(new Vector2(rect.xMax,rect.yMin)));
                var c=container.WorldToLocal(parent.LocalToWorld(rect.max));
                var d=container.WorldToLocal(parent.LocalToWorld(new Vector2(rect.xMin,rect.yMax)));
                bounds=Rect.MinMaxRect(Mathf.Min(a.x,b.x,c.x,d.x),Mathf.Min(a.y,b.y,c.y,d.y),Mathf.Max(a.x,b.x,c.x,d.x),Mathf.Max(a.y,b.y,c.y,d.y));
            }
            else if(boundary is not (null or "" or "content"))
            {throw new InvalidOperationException("--urui-stagger-boundary 必须为 content 或 parent。");}
            prepared=true;activated=false;
            foreach(var child in container.Hierarchy.Children())
            {
                if(child.TagName=="#text"||!child.HasLayout||child.ResolvedStyle.Get("display")=="none"){continue;}
                var entry=new Entry{element=child,previousDistance=child.Style.Get("--urui-stagger-distance"),previousClass=child.ClassListContains(revealedClass)};
                entries.Add(entry);
                var box=child.Layout;
                var distance=edge switch
                {
                    "left"=>bounds.xMin-box.x-box.width,"right"=>bounds.xMax-box.x,
                    "top"=>bounds.yMin-box.y-box.height,_=>bounds.yMax-box.y
                };
                child.Style.SetPixels("--urui-stagger-distance",distance);child.RemoveFromClassList(revealedClass);
            }
            gap=entries.Count>1?Math.Min(maximumGap,span/(entries.Count-1)):0;
        }
        double Number(string property)
        {
            var value=document.Styles.GetComputed(container).GetNumber(property);
            if(value<0)throw new InvalidOperationException("作者进场参数必须为有限非负数："+property);
            return value;
        }
        void Clear()
        {
            foreach(var entry in entries)
            {
                if(!entry.element.IsValid){continue;}
                if(entry.previousDistance==null){entry.element.Style.Remove("--urui-stagger-distance");}
                else{entry.element.Style.Set("--urui-stagger-distance",entry.previousDistance);}
                entry.element.EnableInClassList(revealedClass,entry.previousClass);
            }
            entries.Clear();revealed=0;prepared=false;activated=false;
        }
        void Unloaded(Document owner)=>Dispose();
        /// <summary>停止序列并恢复原有数值覆盖及 class 状态。</summary>
        public void Dispose()
        {
            if(disposed){return;}disposed=true;document.LayoutUpdated-=Update;document.Unloaded-=Unloaded;Clear();
        }
    }
}
