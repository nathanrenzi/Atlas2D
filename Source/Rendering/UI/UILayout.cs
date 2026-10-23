using System.Numerics;

namespace Atlas2D.UI
{
    /// <summary>
    /// Defines the base class for UI layout strategies that control how child elements are measured and arranged.
    /// </summary>
    public abstract class UILayout
    {
        private List<UIElement> _registeredElements = new List<UIElement>();

        internal void RegisterElement(UIElement element)
        {
            if(!_registeredElements.Contains(element))
                _registeredElements.Add(element);
        }

        internal void UnregisterElement(UIElement element)
        {
            _registeredElements.Remove(element);
        }

        /// <summary>
        /// Measures the children of <paramref name="parent"/> and returns the size the parent wants, including its padding.
        /// Call Measure on each visible child. Skip children with IgnoreLayout set, the element measures those itself.
        /// </summary>
        protected internal abstract Vector2 Measure(UIElement parent, Vector2 availableSize);

        /// <summary>
        /// Positions the children of <paramref name="parent"/>. Call Arrange on each visible child with a rect relative to the
        /// parent's top left corner. The parent's size is finalRect.Width and finalRect.Height, and its padding is not yet applied.
        /// Skip children with IgnoreLayout set, the element arranges those itself.
        /// </summary>
        protected internal abstract void Arrange(UIElement parent, Rect finalRect);

        protected void InvalidateMeasure()
        {
            foreach(UIElement element in _registeredElements)
            {
                element.InvalidateMeasure();
            }
        }

        protected void InvalidateArrange()
        {
            foreach (UIElement element in _registeredElements)
            {
                element.InvalidateArrange();
            }
        }
    }
}
