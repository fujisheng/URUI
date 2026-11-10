using System;
using UnityEngine;
namespace URUI.Styles
{
    /// <summary>效果视图中的类型化值；不参与文档 CSS 的级联或继承。</summary>
    public readonly struct StyleOverride
    {
        internal int Kind {get;}
        internal Vector4 Value {get;}
        internal string Keyword {get;}
        StyleOverride(int kind,Vector4 value,string keyword=null){Kind=kind;Value=value;Keyword=keyword;}
        public static StyleOverride Number(float value)
        {if(!float.IsFinite(value))throw new ArgumentOutOfRangeException(nameof(value));return new(1,new Vector4(value,0,0,0));}
        public static StyleOverride Color(Color value)
        {for(var i=0;i<4;i++)if(!float.IsFinite(value[i]))throw new ArgumentOutOfRangeException(nameof(value));return new(4,value);}
        public static StyleOverride FromKeyword(string value)
        {if(string.IsNullOrWhiteSpace(value))throw new ArgumentException("关键词不能为空。",nameof(value));return new(8,default,value);}
    }
}
