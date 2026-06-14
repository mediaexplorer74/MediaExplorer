using System;
using System.Collections.Generic;
using Windows.Foundation;
using Windows.UI.Xaml;

namespace BrowserCore.Engine.Core
{
    public abstract class RenderObject
    {
        public RenderObject Parent { get; set; }
        public List<RenderObject> Children { get; } = new List<RenderObject>();
        public LiteElement Node { get; set; }
        public CssComputed Style { get; set; }
        public Rect Bounds { get; set; }
        public Thickness Margin { get; set; }
        public Thickness Padding { get; set; }
        public Thickness Border { get; set; }

        public bool IsDirty { get; set; }
        public bool IsDetached { get; set; }

        // Table cell grid position (populated by RenderTreeBuilder for TD/TH cells)
        public int TableRow { get; set; }
        public int TableCol { get; set; }
        public int TableRowSpan { get; set; } = 1;
        public int TableColSpan { get; set; } = 1;

        public void AddChild(RenderObject child)
        {
            child.Parent = this;
            Children.Add(child);
        }

        public void MarkDirty()
        {
            IsDirty = true;
            var p = Parent;
            while (p != null) { p.IsDirty = true; p = p.Parent; }
        }

        public void RemoveChild(RenderObject child)
        {
            child.IsDetached = true;
            child.Parent = null;
            Children.Remove(child);
        }

        public void ReplaceChild(RenderObject oldChild, RenderObject newChild)
        {
            int idx = Children.IndexOf(oldChild);
            if (idx >= 0)
            {
                oldChild.IsDetached = true;
                oldChild.Parent = null;
                newChild.Parent = this;
                Children[idx] = newChild;
                MarkDirty();
            }
        }

        public void InsertChild(int index, RenderObject child)
        {
            child.Parent = this;
            Children.Insert(index, child);
            MarkDirty();
        }

        public abstract void Layout(Size availableSize);
    }
}
