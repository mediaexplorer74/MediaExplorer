using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        /// <summary>
        /// Mark a subtree as dirty for layout invalidation.
        /// Propagates dirty flag to all descendants.
        /// </summary>
        public static void InvalidateSubtree(RenderObject node)
        {
            if (node == null) return;
            node.MarkDirty(); // marks node + ancestors
            MarkSubtreeDirty(node);
        }

        /// <summary>
        /// Relayout only the dirty subtree. Async wrapper for UI thread dispatch.
        /// </summary>
        public static async Task RelayoutSubtreeAsync(RenderObject root, Size viewportSize, Func<Task> uiThreadDispatch)
        {
            if (root == null) return;
            await uiThreadDispatch().ConfigureAwait(false);
            PerformIncrementalLayout(root, viewportSize);
        }

        private static void MarkSubtreeDirty(RenderObject node)
        {
            if (node == null) return;
            node.IsDirty = true;
            if (node.Children != null)
                for (int i = 0; i < node.Children.Count; i++)
                    MarkSubtreeDirty(node.Children[i]);
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
