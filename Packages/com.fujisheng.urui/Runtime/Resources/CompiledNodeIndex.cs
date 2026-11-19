using System;
using UnityEngine;

namespace URUI.Resources
{
    /// <summary>
    /// Editor 生成的简单选择器索引；运行时只做区间和二分查找。
    /// </summary>
    [Serializable]
    internal sealed class CompiledNodeIndex
    {
        [Serializable]
        internal sealed class Selector
        {
            [SerializeField]
            internal string key;
            [SerializeField]
            internal string queryKey;
            [SerializeField]
            internal int[] nodes;
        }
        [SerializeField]
        ulong identity;
        [SerializeField]
        int[] subtreeEnds = Array.Empty<int>();
        [SerializeField]
        Selector[] selectors = Array.Empty<Selector>();
        internal ulong Identity => identity;
        internal int NodeCount => subtreeEnds.Length;
        internal void Initialize(ulong key, int[] ends, Selector[] entries)
        {
            identity = key;
            subtreeEnds = ends;
            selectors = entries;
        }

        internal bool TryRange(uint node, out int first, out int end)
        {
            if (node == uint.MaxValue)
            {
                first = 0;
                end = subtreeEnds.Length;
                return true;
            }
            first = checked((int)node);
            if (first < 0 || first >= subtreeEnds.Length)
            {
                end = 0;
                return false;
            }
            end = subtreeEnds[first];
            return end > first && end <= subtreeEnds.Length;
        }
        internal ArraySegment<int> Query(string key, int first, int end, out string queryKey)
        {
            queryKey = null;
            var low = 0;
            var high = selectors.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (string.CompareOrdinal(selectors[middle].key, key) < 0)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            if (low == selectors.Length || selectors[low].key != key)
            {
                return default;
            }
            queryKey = selectors[low].queryKey;
            var nodes = selectors[low].nodes;
            var start = LowerBound(nodes, first);
            var finish = LowerBound(nodes, end);
            return new ArraySegment<int>(nodes, start, finish - start);
        }
        static int LowerBound(int[] values, int value)
        {
            var low = 0;
            var high = values.Length;
            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (values[middle] < value)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }
            return low;
        }
    }
}
