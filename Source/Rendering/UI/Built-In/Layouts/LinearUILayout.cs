using System.Numerics;

namespace Atlas2D.UI
{
    /// <summary>
    /// A base class for UI layouts that arrange children along a single primary axis
    /// (main axis) with alignment control on both the main and cross axes.
    /// </summary>
    public abstract class LinearUILayout : UILayout
    {
        /// <summary>
        /// Gets or sets the fixed spacing in pixels between consecutive children.
        /// Ignored when <see cref="SpaceBetween"/> is <see langword="true"/>.
        /// </summary>
        public float Spacing
        {
            get => _spacing;
            set
            {
                if (_spacing != value)
                {
                    _spacing = value;
                    InvalidateMeasure();
                }
            }
        }
        private float _spacing;


        /// <summary>
        /// Gets or sets how children are aligned along the main axis.
        /// </summary>
        /// <remarks>
        /// When set to <see cref="UILayoutAlignment.Stretch"/>, children without an explicit
        /// size are expanded to fill the available main-axis space equally.
        /// </remarks>
        public UILayoutAlignment MainAxisAlignment
        {
            get => _mainAxisAlignment;
            set
            {
                if (_mainAxisAlignment != value)
                {
                    _mainAxisAlignment = value;
                    InvalidateMeasure();
                }
            }
        }
        private UILayoutAlignment _mainAxisAlignment;


        /// <summary>
        /// Gets or sets how children are aligned along the cross axis.
        /// </summary>
        /// <remarks>
        /// When set to <see cref="UILayoutAlignment.Stretch"/>, children are expanded to
        /// fill the full cross-axis extent of the layout.
        /// </remarks>
        public UILayoutAlignment CrossAxisAlignment
        {
            get => _crossAxisAlignment;
            set
            {
                if (_crossAxisAlignment != value)
                {
                    _crossAxisAlignment = value;
                    InvalidateMeasure();
                }
            }
        }
        private UILayoutAlignment _crossAxisAlignment;

        /// <summary>
        /// Gets or sets whether spacing between children is distributed evenly across
        /// the available main-axis space, ignoring the <see cref="Spacing"/> value.
        /// </summary>
        public bool SpaceBetween
        {
            get => _spaceBetween;
            set
            {
                if (_spaceBetween != value)
                {
                    _spaceBetween = value;
                    InvalidateMeasure();
                }
            }
        }
        private bool _spaceBetween;

        /// <summary>
        /// Gets or sets whether children are arranged in reverse order along the main axis.
        /// </summary>
        public bool ReverseOrder
        {
            get => _reverseOrder;
            set
            {
                if (_reverseOrder != value)
                {
                    _reverseOrder = value;
                    InvalidateArrange();
                }
            }
        }
        private bool _reverseOrder;

        /// <summary>
        /// Extracts the main-axis component from a vector.
        /// </summary>
        protected abstract float GetMain(Vector2 v);

        /// <summary>
        /// Extracts the cross-axis component from a vector.
        /// </summary>
        protected abstract float GetCross(Vector2 v);

        /// <summary>
        /// Constructs a vector from separate main/cross-axis values.
        /// </summary>
        protected abstract Vector2 MakeVector(float main, float cross);

        protected internal override Vector2 Measure(UIElement parent, Vector2 availableSize)
        {
            Vector2 innerSize = new Vector2(availableSize.X - parent.Padding.Left - parent.Padding.Right,
                availableSize.Y - parent.Padding.Top - parent.Padding.Bottom);

            float remainingMain = GetMain(innerSize);
            float totalMeasuredMain = 0;
            float maxCross = 0;
            int total = 0;

            foreach (var child in parent.Children)
            {
                if (!child.Visible || child.IgnoreLayout) continue;

                float availableCross = CrossAxisAlignment == UILayoutAlignment.Stretch
                    ? GetCross(innerSize)
                    : float.MaxValue;

                Vector2 childAvailable = MakeVector(Math.Max(0, remainingMain), availableCross);
                Vector2 childDesired = child.Measure(childAvailable);

                float childMain = GetMain(childDesired);
                totalMeasuredMain += childMain;
                remainingMain -= childMain + Spacing;
                maxCross = Math.Max(maxCross, GetCross(childDesired));
                total++;
            }

            if(total > 0)
            {
                totalMeasuredMain += (total - 1) * Spacing;
            }

            return MakeVector(totalMeasuredMain, maxCross)
                + new Vector2(parent.Padding.Left + parent.Padding.Right,
                parent.Padding.Top + parent.Padding.Bottom);
        }

        protected internal override void Arrange(UIElement parent, Rect finalRect)
        {
            Rect innerRect = new Rect(
                parent.Padding.Left,
                parent.Padding.Top,
                Math.Max(0, finalRect.Width - parent.Padding.Left - parent.Padding.Right),
                Math.Max(0, finalRect.Height - parent.Padding.Top - parent.Padding.Bottom)
            );

            float innerMain = GetMain(new Vector2(innerRect.Width, innerRect.Height));
            float innerCross = GetCross(new Vector2(innerRect.Width, innerRect.Height));

            List<UIElement> children = new List<UIElement>();
            foreach (var child in parent.Children)
            {
                if (!child.Visible || child.IgnoreLayout) continue;
                children.Add(child);
            }

            if (ReverseOrder) children.Reverse();
            if (children.Count == 0) return;

            float totalDesiredMain = 0;
            foreach (var child in children)
                totalDesiredMain += GetMain(child.DesiredSize);

            float spacing = Spacing;
            if (SpaceBetween && children.Count > 1)
                spacing = Math.Max(0, (innerMain - totalDesiredMain) / (children.Count - 1));

            float totalSpacing = children.Count > 1 ? spacing * (children.Count - 1) : 0;
            float totalMain = totalDesiredMain + totalSpacing;

            // Two-pass stretch calculation
            float fixedMain = 0f;
            int flexCount = 0;

            if (MainAxisAlignment == UILayoutAlignment.Stretch)
            {
                foreach (var child in children)
                {
                    float desired = GetMain(child.DesiredSize);
                    bool isFixed = child.ExplicitSize.HasValue
                        || (child.MinSize != Vector2.Zero && GetMain(child.MinSize) >= desired)
                        || (GetMain(child.MaxSize) <= desired);

                    if (isFixed)
                        fixedMain += desired;
                    else
                        flexCount++;
                }
            }

            float availableForFlex = Math.Max(0, innerMain - totalSpacing - fixedMain);
            float stretchMainPerFlex = flexCount > 0 ? availableForFlex / flexCount : 0f;

            float mainStart = GetMain(new Vector2(innerRect.X, innerRect.Y));
            switch (MainAxisAlignment)
            {
                case UILayoutAlignment.Center:
                    mainStart += (innerMain - totalMain) / 2f;
                    break;
                case UILayoutAlignment.End:
                    mainStart += innerMain - totalMain;
                    break;
            }

            float crossStart = GetCross(new Vector2(innerRect.X, innerRect.Y));
            float currentMain = mainStart;

            foreach (var child in children)
            {
                float slotMain;
                if (MainAxisAlignment == UILayoutAlignment.Stretch)
                {
                    float desired = GetMain(child.DesiredSize);
                    bool isFixed = child.ExplicitSize.HasValue
                        || (child.MinSize != Vector2.Zero && GetMain(child.MinSize) >= desired)
                        || (GetMain(child.MaxSize) <= desired);

                    slotMain = isFixed ? desired : stretchMainPerFlex;
                }
                else
                {
                    slotMain = GetMain(child.DesiredSize);
                }

                float slotCross;
                float slotCrossOffset;

                switch (CrossAxisAlignment)
                {
                    case UILayoutAlignment.Stretch:
                        slotCross = innerCross;
                        slotCrossOffset = crossStart;
                        break;
                    case UILayoutAlignment.Center:
                        slotCross = GetCross(child.DesiredSize);
                        slotCrossOffset = crossStart + (innerCross - slotCross) / 2f;
                        break;
                    case UILayoutAlignment.End:
                        slotCross = GetCross(child.DesiredSize);
                        slotCrossOffset = crossStart + innerCross - slotCross;
                        break;
                    default:
                        slotCross = GetCross(child.DesiredSize);
                        slotCrossOffset = crossStart;
                        break;
                }

                Vector2 childPos = MakeVector((int)currentMain, (int)slotCrossOffset);
                Vector2 childSize = MakeVector(slotMain, slotCross);

                child.Arrange(CalculateAnchoredRect(child, new Rect(childPos.X, childPos.Y, childSize.X, childSize.Y)));

                currentMain += slotMain + spacing;
            }
        }

        private Rect CalculateAnchoredRect(UIElement child, Rect rect)
        {
            return new Rect(
                rect.X + child.Anchor.X * rect.Width,
                rect.Y + child.Anchor.Y * rect.Height,
                rect.Width,
                rect.Height
            );
        }
    }
}