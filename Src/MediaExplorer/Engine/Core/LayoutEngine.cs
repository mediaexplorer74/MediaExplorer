using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace BrowserCore.Engine.Core
{
    public static class LayoutEngine
    {
        public static void PerformLayout(RenderObject root, Size viewportSize)
        {
            if (root == null) return;
            root.Layout(viewportSize);
        }

        public static void PerformIncrementalLayout(RenderObject root, Size viewportSize)
        {
            if (root == null) return;
            var dirtyNodes = new List<RenderObject>();
            CollectDirty(root, dirtyNodes);

            if (dirtyNodes.Count == 0) return;

            var processed = new HashSet<RenderObject>();
            foreach (var node in dirtyNodes)
            {
                var ancestor = node;
                while (ancestor != null && !processed.Contains(ancestor))
                {
                    processed.Add(ancestor);
                    ancestor = ancestor.Parent;
                }
                if (processed.Contains(node)) continue;
                node.Layout(viewportSize);
                processed.Add(node);
                node.IsDirty = false;
                ClearDirtyDescendants(node);
            }
        }

        private static void CollectDirty(RenderObject node, List<RenderObject> result)
        {
            if (node == null || node.IsDetached) return;
            if (node.IsDirty) { result.Add(node); return; }
            if (node.Children != null)
                for (int i = 0; i < node.Children.Count; i++)
                    CollectDirty(node.Children[i], result);
        }

        private static void ClearDirtyDescendants(RenderObject node)
        {
            if (node == null) return;
            node.IsDirty = false;
            if (node.Children != null)
                for (int i = 0; i < node.Children.Count; i++)
                    ClearDirtyDescendants(node.Children[i]);
        }
    }
}
