using Atlas2D.Rendering;
using Atlas2D.Rendering.Shaders;
using GLFW;

using System.Numerics;
using static Atlas2D.OpenGL.GL;

namespace Atlas2D.UI
{
    public abstract class UIElement : IRenderable
    {
        /// <summary>
        /// Fired after the UIElement has completed both measure and arrange passes.
        /// </summary>
        public event Action OnLayoutComplete;

        /// <summary>
        /// Gets the parent UIElement of this element in the UI hierarchy.
        /// </summary>
        public UIElement Parent { get; private set; }

        /// <summary>
        /// Gets a read-only collection of child UIElements.
        /// </summary>
        public IReadOnlyList<UIElement> Children => _children;
        private List<UIElement> _children = new List<UIElement>();

        /// <summary>
        /// Gets or sets the position of this element relative to its parent.
        /// Setting this property invalidates arrangement.
        /// </summary>
        public Vector2 Position
        {
            get => _position;
            set
            {
                if (_position != value)
                {
                    _position = value;
                    Parent?.InvalidateArrange();
                }
            }
        }
        private Vector2 _position;

        /// <summary>
        /// Gets the actual size calculated during the arrange pass.
        /// </summary>
        public Vector2 Size { get; private set; }

        /// <summary>
        /// Gets the desired size calculated during the measure pass.
        /// </summary>
        public Vector2 DesiredSize { get; private set; }

        /// <summary>
        /// Gets or sets an explicit size for this element, overriding content-driven sizing during the measure pass.
        /// When set, the element will always report this as its desired size, bypassing layout and child measurement.
        /// Set to null to return to content-driven sizing.
        /// </summary>
        public Vector2? ExplicitSize
        {
            get => _explicitSize;
            set
            {
                if (_explicitSize != value)
                {
                    _explicitSize = value;
                    InvalidateMeasure();
                    UpdateMesh();
                }
            }
        }
        private Vector2? _explicitSize;

        /// <summary>
        /// Gets or sets the pivot point for this element, expressed as a normalized value (0-1).
        /// The pivot determines the origin point for positioning and transformations.
        /// Setting this property updates the mesh.
        /// </summary>
        public Vector2 Pivot
        {
            get => _pivot;
            set
            {
                if(_pivot != value)
                {
                    _pivot = value;
                    UpdateMesh();
                }
            }
        }
        private Vector2 _pivot = Vector2.Zero;

        /// <summary>
        /// Gets or sets the anchor point within the parent element, expressed as a normalized value (0-1).
        /// The anchor determines where this element is positioned within its parent's bounds.
        /// Setting this property invalidates arrangement.
        /// </summary>
        public Vector2 Anchor
        {
            get => _anchor;
            set
            {
                if (_anchor != value)
                {
                    _anchor = value;
                    Parent?.InvalidateArrange();
                }
            }
        }
        private Vector2 _anchor = Vector2.Zero;

        /// <summary>
        /// Gets or sets the margin (outer spacing) around this element.
        /// Setting this property invalidates measurement and updates the mesh.
        /// </summary>
        public Border Margin
        {
            get => _margin;
            set
            {
                _margin = value;
                InvalidateMeasure();
                UpdateMesh();
            }
        }
        private Border _margin;

        /// <summary>
        /// Gets or sets the padding (inner spacing) within this element.
        /// Setting this property invalidates measurement and updates the mesh.
        /// </summary>
        public Border Padding
        {
            get => _padding;
            set
            {
                _padding = value;
                InvalidateMeasure();
                UpdateMesh();
            }
        }
        private Border _padding;

        /// <summary>
        /// Gets or sets the minimum size constraints for this element.
        /// Setting this property invalidates measurement and updates the mesh.
        /// </summary>
        public Vector2 MinSize
        {
            get => _minSize;
            set
            {
                if(_minSize != value)
                {
                    _minSize = value;
                    InvalidateMeasure();
                    UpdateMesh();
                }
            }
        }
        private Vector2 _minSize = Vector2.Zero;

        /// <summary>
        /// Gets or sets the maximum size constraints for this element.
        /// Setting this property invalidates measurement and updates the mesh.
        /// </summary>
        public Vector2 MaxSize
        {
            get => _maxSize;
            set
            {
                if(_maxSize != value)
                {
                    _maxSize = value;
                    InvalidateMeasure();
                    UpdateMesh();
                }
            }
        }
        private Vector2 _maxSize = new Vector2(float.MaxValue, float.MaxValue);

        /// <summary>
        /// Gets or sets additional pixels to extend the interaction area beyond the visual bounds.
        /// Useful for making small UI elements easier to click.
        /// </summary>
        public float ExtraInteractionPixels { get; set; }

        /// <summary>
        /// Gets or sets whether this element is visible.
        /// When visibility changes, GainVisibility or LoseVisibility events are raised.
        /// </summary>
        public bool Visible
        {
            get => _visible;
            set
            {
                if(_visible != value)
                {
                    _visible = value;
                    UIEvent evt = new UIEvent()
                    {
                        Type = value ? UIEventType.GainVisibility : UIEventType.LoseVisibility,
                        Target = this,
                        Phase = EventPhase.Target
                    };
                    RaiseEvent(evt);
                    if (value)
                    {
                        IsMeasureValid = false;
                        IsArrangeValid = false;
                        Parent?.InvalidateMeasure();
                    }
                }
            }
        }
        private bool _visible = true;

        /// <summary>
        /// Gets or sets whether this element is enabled.
        /// </summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// Gets or sets whether this element can be interacted with.
        /// When interactability changes, GainInteractability or LoseInteractability events are raised.
        /// </summary>
        public bool Interactable
        {
            get => _interactable;
            set
            {
                if(_interactable != value)
                {
                    _interactable = value;
                    UIEvent evt = new UIEvent()
                    {
                        Type = value ? UIEventType.GainInteractability : UIEventType.LoseInteractability,
                        Target = this,
                        Phase = EventPhase.Target
                    };
                    RaiseEvent(evt);
                }
            }
        }
        private bool _interactable = true;

        /// <summary>
        /// Gets or sets whether this element ignores the layout of its parent element.
        /// </summary>
        public bool IgnoreLayout
        {
            get => _ignoreLayout;
            set
            {
                if (_ignoreLayout != value)
                {
                    _ignoreLayout = value;
                    InvalidateMeasure();
                    UpdateMesh();
                }
            }
        }
        private bool _ignoreLayout;

        /// <summary>
        /// Gets whether this element currently has focus.
        /// </summary>
        public bool Focused { get; internal set; }

        /// <summary>
        /// Gets whether the measure pass results are still valid.
        /// </summary>
        public bool IsMeasureValid { get; private set; }

        /// <summary>
        /// Gets whether the arrange pass results are still valid.
        /// </summary>
        public bool IsArrangeValid { get; private set; }

        /// <summary>
        /// Gets or sets the layout strategy used to position and size child elements.
        /// </summary>
        public UILayout Layout
        {
            get => _layout;
            set
            {
                if (_layout == value) return;
                _layout?.UnregisterElement(this);
                _layout = value;
                _layout?.RegisterElement(this);
                InvalidateMeasure();
            }
        }
        private UILayout _layout;

        /// <summary>
        /// Gets the mesh renderer used to draw this element (if it exists).
        /// </summary>
        public MeshRenderer Renderer { get; protected set; }

        /// <summary>
        /// Gets the rendering layer order for this element.
        /// </summary>
        public int RenderLayer { get; private set; }

        /// <summary>
        /// Gets or sets whether to use world-space for rendering. This value propogates to children.
        /// </summary>
        public bool UseWorldPosition
        {
            get => _useWorldPosition;
            private set
            {
                if(_useWorldPosition != value)
                {
                    _useWorldPosition = value;
                    if (Renderer != null) Renderer.UseUnscaledProjectionMatrix = !value;
                    for (int i = 0; i < _children.Count; i++)
                    {
                        _children[i].UseWorldPosition = value;
                    }
                }
            }
        }
        private bool _useWorldPosition;

        /// <summary>
        /// Gets whether this element ignores post-processing effects.
        /// </summary>
        public bool IgnorePostProcessing { get; }

        /// <summary>
        /// Gets or sets whether to snap this element to screen pixels. Only considered when not using world space.
        /// </summary>
        public bool SnapToPixels { get; set; }

        /// <summary>
        /// Gets or sets whether this element can have children added to it.
        /// </summary>
        public bool CanAddChildren { get; set; } = true;

        /// <summary>
        /// Gets whether this element clips its children to its own bounds using the stencil buffer.
        /// </summary>
        public bool Mask { get; }

        private bool _useMeshRenderer;
        private bool _disposed;
        private Rect? _lastArrangeRect;
        private CursorType _hoverCursorType = CursorType.Arrow;
        private List<IConstraint> _constraints = new List<IConstraint>();
        private Dictionary<UIEventType, List<Action<UIEvent>>> _eventHandlers = new Dictionary<UIEventType, List<Action<UIEvent>>>();


        /// <summary>
        /// Initializes a new instance of the UIElement class.
        /// </summary>
        /// <param name="arguments">Optional rendering arguments.</param>
        /// <param name="useMeshRenderer">Whether to create a mesh renderer for this element.</param>
        public UIElement(UIRenderArgs? arguments, bool useMeshRenderer)
        {
            if(!arguments.HasValue)
            {
                arguments = new UIRenderArgs();
            }

            _position = Vector2.Zero;
            DesiredSize = Vector2.Zero;

            RenderLayer = arguments.Value.RenderLayer;
            _useWorldPosition = arguments.Value.UseWorldPosition;
            IgnorePostProcessing = arguments.Value.IgnorePostProcessing;
            Mask = arguments.Value.Mask;
            SnapToPixels = arguments.Value.SnapToPixels;
            _useMeshRenderer = useMeshRenderer;

            if (_useMeshRenderer)
            {
                SharedResource<Material> material = SharedResource<Material>.Create(Material.Create(GlobalShaders.Interface));
                Renderer = new MeshRenderer(material)
                {
                    UseUnscaledProjectionMatrix = !UseWorldPosition,
                    UseStencilBuffer = true
                };
                material.Release();
            }

            UICanvas.Instance?.RegisterUIElement(this);
            RenderLayerManager.OrderRenderable(this);
        }

        /// <summary>
        /// Adds a child element to this element.
        /// If the child already has a parent, it will be removed from that parent first.
        /// </summary>
        /// <param name="child">The child element to add.</param>
        public void AddChild(UIElement child)
        {
            if (!CanAddChildren) return;
            if (child.Parent != null)
                child.Parent.RemoveChild(child);

            RenderLayerManager.RemoveRenderable(child);
            _children.Add(child);
            child.Parent = this;
            child.UseWorldPosition = _useWorldPosition;
            UICanvas.Instance.OnElementParented(child);
            InvalidateMeasure();
        }

        /// <summary>
        /// Removes a child element from this element.
        /// </summary>
        /// <param name="child">The child element to remove.</param>
        public void RemoveChild(UIElement child)
        {
            if (_children.Remove(child))
            {
                RenderLayerManager.OrderRenderable(child);
                child.Parent = null;
                UICanvas.Instance.OnElementUnparented(child);
                InvalidateMeasure();
            }
        }

        /// <summary>
        /// Removes all child elements from this element.
        /// </summary>
        public void ClearChildren()
        {
            foreach (var child in _children)
            {
                RenderLayerManager.OrderRenderable(child);
                UICanvas.Instance.RegisterUIElement(child);
                child.Parent = null;
            }
            _children.Clear();
            InvalidateMeasure();
        }


        /// <summary>
        /// Invalidates the measure pass, forcing a recalculation of desired size.
        /// This also invalidates the arrange pass and propagates up to the parent.
        /// </summary>
        public void InvalidateMeasure()
        {
            if (!IsMeasureValid) return;

            IsMeasureValid = false;
            IsArrangeValid = false;

            Parent?.InvalidateMeasure();
        }

        /// <summary>
        /// Invalidates the arrange pass, forcing a recalculation of final layout.
        /// </summary>
        public void InvalidateArrange()
        {
            if (!IsArrangeValid) return;
            IsArrangeValid = false;
            UICanvas.Instance?.QueueArrange(this);
        }

        internal void ArrangeFromLastRect()
        {
            if (_disposed || IsArrangeValid || !_lastArrangeRect.HasValue) return;
            Arrange(_lastArrangeRect.Value);
        }

        /// <summary>
        /// Measure pass. Calculate desired size given available space.
        /// </summary>
        /// <param name="availableSize">The available space provided by the parent.</param>
        /// <returns>The desired size of this element including margins.</returns>
        public Vector2 Measure(Vector2 availableSize)
        {
            if (!Visible)
            {
                DesiredSize = Vector2.Zero;
                IsMeasureValid = true;
                return DesiredSize;
            }

            availableSize = new Vector2(
                Math.Max(0, availableSize.X - Margin.Left - Margin.Right),
                Math.Max(0, availableSize.Y - Margin.Top - Margin.Bottom)
            );

            Vector2 desiredSize;
            if (Layout != null)
            {
                desiredSize = Layout.Measure(this, availableSize);
            }
            else
            {
                desiredSize = MeasureCore(availableSize);
            }

            if (_explicitSize.HasValue)
            {
                desiredSize = _explicitSize.Value;
            }

            desiredSize = new Vector2(
                Math.Clamp(desiredSize.X, MinSize.X, MaxSize.X),
                Math.Clamp(desiredSize.Y, MinSize.Y, MaxSize.Y)
            );

            DesiredSize = new Vector2(
                desiredSize.X + Margin.Left + Margin.Right,
                desiredSize.Y + Margin.Top + Margin.Bottom
            );

            IsMeasureValid = true;
            return DesiredSize;
        }

        /// <summary>
        /// Core measure logic for this element. Override to customize measurement behavior.
        /// </summary>
        /// <param name="availableSize">The available space after margins have been subtracted.</param>
        /// <returns>The desired size before margins are added.</returns>
        protected virtual Vector2 MeasureCore(Vector2 availableSize)
        {
            availableSize = new Vector2(
                Math.Max(0, availableSize.X - Padding.Left - Padding.Right),
                Math.Max(0, availableSize.Y - Padding.Top - Padding.Bottom)
            );

            Vector2 maxChildSize = Vector2.Zero;
            foreach (var child in _children)
            {
                if (!child.Visible) continue;
                if (child.IgnoreLayout)
                {
                    // Measure but don't contribute to size
                    child.Measure(availableSize);
                    continue;
                }
                var childDesired = child.Measure(availableSize);
                maxChildSize = Vector2.Max(maxChildSize, childDesired);
            }

            return new Vector2(
                maxChildSize.X + Padding.Left + Padding.Right,
                maxChildSize.Y + Padding.Top + Padding.Bottom
            );
        }

        /// <summary>
        /// Arrange pass. Assign final positions and sizes to this element and its children.
        /// </summary>
        /// <param name="finalRect">The final rectangle allocated to this element by its parent.</param>
        public void Arrange(Rect finalRect)
        {
            _lastArrangeRect = finalRect;

            if (!Visible)
            {
                IsArrangeValid = true;
                return;
            }

            finalRect = new Rect(
                finalRect.X + Margin.Left,
                finalRect.Y + Margin.Top,
                Math.Max(0, finalRect.Width - Margin.Left - Margin.Right),
                Math.Max(0, finalRect.Height - Margin.Top - Margin.Bottom)
            );

            if (_explicitSize.HasValue)
            {
                finalRect.Width = _explicitSize.Value.X;
                finalRect.Height = _explicitSize.Value.Y;
            }

            finalRect.Width = Math.Clamp(finalRect.Width, MinSize.X, MaxSize.X);
            finalRect.Height = Math.Clamp(finalRect.Height, MinSize.Y, MaxSize.Y);

            Size = new Vector2(finalRect.Width, finalRect.Height);

            if (Parent != null)
            {
                _position = new Vector2(finalRect.X, finalRect.Y);
            }

            if (Layout != null)
            {
                Layout.Arrange(this, finalRect);
            }
            else
            {
                ArrangeCore(finalRect);
            }

            foreach (var constraint in _constraints)
            {
                constraint.Apply(this);
            }

            UpdateMesh();
            OnLayoutComplete?.Invoke();

            IsArrangeValid = true;
        }

        /// <summary>
        /// Core arrange logic for this element. Override to customize arrangement behavior.
        /// </summary>
        /// <param name="finalRect">The final rectangle after margins have been applied.</param>
        protected virtual void ArrangeCore(Rect finalRect)
        {
            Rect childRect = new Rect(
                Padding.Left,
                Padding.Top,
                Math.Max(0, finalRect.Width - Padding.Left - Padding.Right),
                Math.Max(0, finalRect.Height - Padding.Top - Padding.Bottom)
            );

            foreach (var child in _children)
            {
                if (!child.Visible || child.IgnoreLayout) continue;
                child.Arrange(CalculateAnchoredRect(child, childRect));
            }

            // For children with IgnoreLayout = true
            foreach (var child in _children)
            {
                if (!child.Visible || !child.IgnoreLayout) continue;
                child.Arrange(new Rect(child.Position.X, child.Position.Y,
                    child.DesiredSize.X, child.DesiredSize.Y));
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


        /// <summary>
        /// Adds a constraint that will be applied to this element during arrangement.
        /// </summary>
        /// <param name="constraint">The constraint to add.</param>
        public void AddConstraint(IConstraint constraint)
        {
            _constraints.Add(constraint);
            InvalidateArrange();
        }

        /// <summary>
        /// Removes a previously added constraint.
        /// </summary>
        /// <param name="constraint">The constraint to remove.</param>
        public void RemoveConstraint(IConstraint constraint)
        {
            _constraints.Remove(constraint);
            InvalidateArrange();
        }

        /// <summary>
        /// Removes all constraints from this element.
        /// </summary>
        public void ClearConstraints()
        {
            _constraints.Clear(); 
            InvalidateArrange();
        }

        /// <summary>
        /// Registers an event handler for a specific UI event type.
        /// </summary>
        /// <param name="type">The type of event to listen for.</param>
        /// <param name="handler">The handler to invoke when the event occurs.</param>
        public void AddEventListener(UIEventType type, Action<UIEvent> handler)
        {
            if (!_eventHandlers.ContainsKey(type))
                _eventHandlers[type] = new List<Action<UIEvent>>();

            _eventHandlers[type].Add(handler);
        }

        /// <summary>
        /// Unregisters a previously registered event handler.
        /// </summary>
        /// <param name="type">The type of event.</param>
        /// <param name="handler">The handler to remove.</param>
        public void RemoveEventListener(UIEventType type, Action<UIEvent> handler)
        {
            if (_eventHandlers.ContainsKey(type))
                _eventHandlers[type].Remove(handler);
        }

        /// <summary>
        /// Raises a UI event on this element, invoking all registered handlers.
        /// The event may bubble up to parent elements if not stopped.
        /// </summary>
        /// <param name="evt">The event to raise.</param>
        public void RaiseEvent(UIEvent evt)
        {
            evt.Current = this;

            if (_eventHandlers.TryGetValue(evt.Type, out var handlers))
            {
                foreach (var handler in handlers.ToArray())
                {
                    handler(evt);
                    if (evt.IsPropagationStopped) return;
                }
            }

            HandleBuiltInEvent(evt);

            if (evt.Phase == EventPhase.Bubble && Parent != null && !evt.IsPropagationStopped)
            {
                Parent.RaiseEvent(evt);
            }
        }

        private void HandleBuiltInEvent(UIEvent evt)
        {
            switch (evt.Type)
            {
                case UIEventType.MouseEnter:
                    if (_hoverCursorType != CursorType.Arrow)
                        Cursor.SetType(_hoverCursorType);
                    break;

                case UIEventType.MouseLeave:
                    if (_hoverCursorType != CursorType.Arrow)
                        Cursor.SetType(CursorType.Arrow);
                    break;
            }
        }

        /// <summary>
        /// Gets the bounding rectangle of this element in virtual coordinates, including extra interaction pixels.
        /// </summary>
        /// <returns>The bounding rectangle in virtual canvas space, including extra interaction pixels.</returns>
        public Rect GetInteractionBounds()
        {
            Vector2 pos = GetVirtualPosition();

            Vector2 pivotOffset = new Vector2(
                -Pivot.X * Size.X,
                -Pivot.Y * Size.Y
            );

            return new Rect(
                pos.X + pivotOffset.X - ExtraInteractionPixels,
                pos.Y + pivotOffset.Y - ExtraInteractionPixels,
                Size.X + ExtraInteractionPixels * 2,
                Size.Y + ExtraInteractionPixels * 2
            );
        }

        /// <summary>
        /// Gets the bounding rectangle of this element in virtual coordinates.
        /// </summary>
        /// <returns>The bounding rectangle in virtual canvas space.</returns>
        public Rect GetVirtualBounds()
        {
            Vector2 pos = GetVirtualPosition();

            Vector2 pivotOffset = new Vector2(
                -Pivot.X * Size.X,
                -Pivot.Y * Size.Y
            );

            return new Rect(
                pos.X + pivotOffset.X,
                pos.Y + pivotOffset.Y,
                Size.X,
                Size.Y
            );
        }


        /// <summary>
        /// Gets the bounding rectangle of this element in local (element-relative) virtual coordinates.
        /// </summary>
        /// <returns>The local bounding rectangle.</returns>
        public Rect GetLocalBounds()
        {
            Vector2 pivotOffset = new Vector2(
                -Pivot.X * Size.X,
                -Pivot.Y * Size.Y
            );

            return new Rect(
                pivotOffset.X,
                pivotOffset.Y,
                Size.X,
                Size.Y
            );
        }

        /// <summary>
        /// Gets the position of this element in virtual coordinates.
        /// </summary>
        /// <returns>The virtual position on the canvas.</returns>
        public Vector2 GetVirtualPosition()
        {
            if (Parent == null)
            {
                return new Vector2(_position.X, UseWorldPosition ? -_position.Y : _position.Y);
            }

            Vector2 parentVirtualPos = Parent.GetVirtualPosition();
            Vector2 parentPivotOffset = new Vector2(
                -Parent.Pivot.X * Parent.Size.X,
                -Parent.Pivot.Y * Parent.Size.Y
            );
            Vector2 parentTopLeft = parentVirtualPos + parentPivotOffset;

            return parentTopLeft + _position;
        }

        /// <summary>
        /// Tests whether a point in virtual coordinates intersects with this element's bounds.
        /// Returns false if the element is not visible, enabled, or interactable.
        /// </summary>
        /// <param name="virtualPoint">The point to test in virtual coordinates.</param>
        /// <returns>True if the point is within this element's bounds, otherwise false.</returns>
        public bool HitTest(Vector2 virtualPoint)
        {
            if (!Visible || !Enabled || !Interactable)
                return false;

            Rect rect = GetInteractionBounds();
            return virtualPoint.X >= rect.X &&
                   virtualPoint.X <= rect.X + rect.Width &&
                   virtualPoint.Y >= rect.Y &&
                   virtualPoint.Y <= rect.Y + rect.Height;
        }

        /// <summary>
        /// Updates the mesh geometry for this element. Must be implemented by derived classes.
        /// </summary>
        public abstract void UpdateMesh();

        /// <summary>
        /// Sets the color of this element's material.
        /// </summary>
        /// <param name="color">The color to apply.</param>
        public virtual void SetColor(Color color)
        {
            if (Renderer != null && Renderer.Material.IsValid)
            {
                Renderer.Material.Value.MainColor = color;
            }
        }

        /// <summary>
        /// Sets the cursor type to display when the mouse hovers over this element.
        /// </summary>
        /// <param name="type">The cursor type to use on hover.</param>
        public void SetHoverCursorType(CursorType type)
        {
            _hoverCursorType = type;
        }

        /// <summary>
        /// Renders this element and all of its children.
        /// </summary>
        public void Render()
        {
            Render(0);
        }

        public virtual void Render(int stencil)
        {
            if (!Visible || !Enabled) return;

            if (Mask)
            {
                // Push - increment stencil
                glColorMask(false, false, false, false);
                RenderSelf(stencil, MaskWriteMode.Push);
                glColorMask(true, true, true, true);

                // Draw self and children clipped to the mask
                RenderSelf(stencil + 1);
                RenderChildren(stencil + 1);

                // Pop - decrement stencil
                glColorMask(false, false, false, false);
                RenderSelf(stencil, MaskWriteMode.Pop);
                glColorMask(true, true, true, true);
            }
            else
            {
                RenderSelf(stencil);
                RenderChildren(stencil);
            }
        }

        protected void RenderSelf(int stencil, MaskWriteMode maskMode = MaskWriteMode.None)
        {
            if (Renderer != null)
            {
                Vector2 pos = GetVirtualPosition();
                if (SnapToPixels && !UseWorldPosition)
                {
                    pos = UICanvas.Instance.VirtualToScreen(pos);
                    pos = new Vector2(MathF.Round(pos.X), MathF.Round(pos.Y));
                    pos = UICanvas.Instance.ScreenToVirtual(pos);
                }

                switch (maskMode)
                {
                    case MaskWriteMode.Push:
                        Renderer.StencilFunction = GL_EQUAL;
                        Renderer.StencilReference = stencil;
                        Renderer.StencilMask = 0xFF;
                        Renderer.StencilFunctionMask = 0xFF;
                        Renderer.StencilFail = GL_KEEP;
                        Renderer.StencilPass = GL_INCR;
                        break;
                    case MaskWriteMode.Pop:
                        Renderer.StencilFunction = GL_EQUAL;
                        Renderer.StencilReference = stencil + 1;
                        Renderer.StencilMask = 0xFF;
                        Renderer.StencilFunctionMask = 0xFF;
                        Renderer.StencilFail = GL_KEEP;
                        Renderer.StencilPass = GL_DECR;
                        break;
                    default:
                        Renderer.StencilFunction = GL_EQUAL;
                        Renderer.StencilReference = stencil;
                        Renderer.StencilMask = 0x00;
                        Renderer.StencilFunctionMask = 0xFF;
                        Renderer.StencilFail = GL_KEEP;
                        Renderer.StencilPass = GL_KEEP;
                        break;
                }

                Renderer.GetMaterial().Value.Shader.Value.Use();
                Renderer.GetMaterial().Value.Shader.Value.SetMatrix4x4("model", !UseWorldPosition ? Matrix4x4.CreateTranslation(pos.X, pos.Y, 0)
                    : Matrix4x4.CreateTranslation(pos.X, -pos.Y, 0) * Matrix4x4.CreateReflection(new Plane(0, 1, 0, pos.Y)));
                Renderer.GetMaterial().Value.Shader.Value.SetMatrix4x4("uiMatrix",
                    !UseWorldPosition ? UICanvas.Instance.UIModelMatrix : Matrix4x4.Identity);
                Renderer.Render();
            }
            else if (Mask && (maskMode == MaskWriteMode.Push || maskMode == MaskWriteMode.Pop))
            {
                // Using UICanvas to render the mask if Renderer == null
                UICanvas.Instance.RenderMask(this, stencil, maskMode);
            }
        }

        protected void RenderChildren(int stencil)
        {
            foreach (var child in _children)
            {
                child.Render(stencil);
            }
        }

        /// <summary>
        /// Gives focus to this element, making it the active element for keyboard input.
        /// </summary>
        public void Focus()
        {
            UICanvas.Instance?.Focus(this);
            Focused = true;
        }


        /// <summary>
        /// Removes focus from this element.
        /// </summary>
        public void Unfocus()
        {
            UICanvas.Instance?.Unfocus(this);
            Focused = false;
        }

        /// <summary>
        /// Sets the explicit size and position of this element in a single operation, avoiding redundant layout passes.
        /// Prefer this over setting <see cref="ExplicitSize"/> and <see cref="Position"/> individually when changing both.
        /// </summary>
        /// <param name="explicitSize">The explicit size to apply.</param>
        /// <param name="position">The position to apply.</param>
        public void SetTransform(Vector2 explicitSize, Vector2 position)
        {
            bool sizeChanged = ExplicitSize != explicitSize;
            bool posChanged = _position != position;

            if (sizeChanged)
            {
                ExplicitSize = explicitSize;
                _position = position;
                InvalidateMeasure();
                UpdateMesh();
            }
            else if (posChanged)
            {
                _position = position;
                Parent?.InvalidateArrange();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (Parent != null)
            {
                Parent._children.Remove(this);
                Parent.InvalidateMeasure();
                Parent = null;
            }

            RenderLayerManager.RemoveRenderable(this);

            foreach (var child in _children.ToArray())
            {
                child.Dispose();
            }

            _constraints.Clear();
            _layout?.UnregisterElement(this);
            OnDispose();
            Renderer?.Dispose();
            UICanvas.Instance?.UnregisterUIElement(this);

        }


        /// <summary>
        /// Called when this element is being disposed. Override to add custom cleanup logic.
        /// </summary>
        protected virtual void OnDispose() { }

        public enum MaskWriteMode
        {
            None,
            Push,
            Pop
        }
    }
}
