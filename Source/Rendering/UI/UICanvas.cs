using Atlas2D.Rendering;
using Atlas2D.Rendering.Shaders;
using System.Numerics;
using static Atlas2D.OpenGL.GL;

namespace Atlas2D.UI
{
    /// <summary>
    /// Manages the UI element tree, input processing, layout, and rendering for the game's user interface.
    /// Only one <see cref="UICanvas"/> can exist at a time.
    /// </summary>
    public sealed class UICanvas
    {
        /// <summary>
        /// The active <see cref="UICanvas"/> instance.
        /// </summary>
        public static UICanvas Instance { get; private set; }

        /// <summary>
        /// Fired when the UI scale changes, for example when the window is resized.
        /// </summary>
        public event Action<float> OnUIScaleChanged;

        /// <summary>
        /// The model matrix used to transform UI elements from virtual space to screen space.
        /// </summary>
        public Matrix4x4 UIModelMatrix { get; private set; }

        /// <summary>
        /// The inverse of <see cref="UIModelMatrix"/>, used to transform from screen space to virtual space.
        /// </summary>
        public Matrix4x4 UIModelMatrixInverse { get; private set; }

        /// <summary>
        /// The virtual resolution of the canvas in pixels.
        /// </summary>
        public Vector2 VirtualResolution { get; private set; }

        /// <summary>
        /// The current uniform UI scale factor derived from <see cref="UIModelMatrix"/>.
        /// </summary>
        public float Scale => UIModelMatrix.M11;

        private List<UIElement> _rootElements = new List<UIElement>();
        private List<UIElement> _allElements = new List<UIElement>();
        private List<UIElement> _arrangeQueue = new List<UIElement>();
        private List<UIElement> _arrangeQueueBuffer = new List<UIElement>();
        private UIElement _focusedElement = null;
        private UIElement _hoveredElement = null;
        private UIElement _draggedElement = null;

        private MeshRenderer _maskRenderer;
        private MeshRenderer _maskRendererWorld;

        private UIScalingMode _scalingMode;
        private bool _maintainAspect;

        private Vector2 _lastMousePosition;

        public UICanvas()
        {
            if (Instance != null)
            {
                Debug.LogError("Only one UICanvas can exist at a time.");
                return;
            }
            Instance = this;

            _scalingMode = ProjectSettings.UICanvasSettings.ScalingMode;
            VirtualResolution = ProjectSettings.UICanvasSettings.VirtualResolution;
            _maintainAspect = ProjectSettings.UICanvasSettings.MaintainAspect;

            UpdateScaling();

            Engine.Game.LateUpdateEvent += Update;
            Display.OnWindowResize += OnWindowResized;

            float[] vertices = {
                0f, 0f, 0f, 0f,
                1f, 0f, 1f, 0f,
                1f, 1f, 1f, 1f,
                0f, 1f, 0f, 1f
            };

            uint[] indices = {
                0, 1, 2,
                2, 3, 0
            };

            SharedResource<Material> stencilMat = SharedResource<Material>.Create(Material.Create(GlobalShaders.StencilOnly));
            _maskRenderer = new MeshRenderer(stencilMat)
            {
                UseUnscaledProjectionMatrix = true,
                UseStencilBuffer = true
            };
            _maskRenderer.SetVertexArrays(vertices, indices);

            _maskRendererWorld = new MeshRenderer(stencilMat)
            {
                UseUnscaledProjectionMatrix = false,
                UseStencilBuffer = true
            };
            _maskRendererWorld.SetVertexArrays(vertices, indices);
            stencilMat.Release();
        }

        /// <summary>
        /// Registers a <see cref="UIElement"/> with the canvas so it receives layout and input processing.
        /// </summary>
        /// <param name="element">The element to register.</param>
        public void RegisterUIElement(UIElement element)
        {
            if (!_allElements.Contains(element))
            {
                _allElements.Add(element);

                if (element.Parent == null)
                    _rootElements.Add(element);
            }
        }

        /// <summary>
        /// Unregisters a <see cref="UIElement"/> from the canvas.
        /// </summary>
        /// <param name="element">The element to unregister.</param>
        public void UnregisterUIElement(UIElement element)
        {
            _allElements.Remove(element);
            _rootElements.Remove(element);

            if (_hoveredElement == element) _hoveredElement = null;
            if (_focusedElement == element) _focusedElement = null;
            if (_draggedElement == element) _draggedElement = null;
        }

        /// <summary>
        /// Renders the stencil mask for a <see cref="UIElement"/> at its current bounds.
        /// </summary>
        /// <param name="element">The element to render a mask for.</param>
        /// <param name="stencil">The current stencil depth.</param>
        /// <param name="maskMode">Whether to push or pop the mask.</param>
        public void RenderMask(UIElement element, int stencil, UIElement.MaskWriteMode maskMode)
        {
            MeshRenderer renderer = element.UseWorldPosition ? _maskRendererWorld : _maskRenderer;

            Vector2 pos = element.GetVirtualPosition();
            Vector2 pivotOffset = new Vector2(-element.Pivot.X * element.Size.X, -element.Pivot.Y * element.Size.Y);
            pos += pivotOffset;

            switch (maskMode)
            {
                case UIElement.MaskWriteMode.Push:
                    renderer.StencilFunction = GL_EQUAL;
                    renderer.StencilReference = stencil;
                    renderer.StencilMask = 0xFF;
                    renderer.StencilFunctionMask = 0xFF;
                    renderer.StencilFail = GL_KEEP;
                    renderer.StencilPass = GL_INCR;
                    break;
                case UIElement.MaskWriteMode.Pop:
                    renderer.StencilFunction = GL_EQUAL;
                    renderer.StencilReference = stencil + 1;
                    renderer.StencilMask = 0xFF;
                    renderer.StencilFunctionMask = 0xFF;
                    renderer.StencilFail = GL_KEEP;
                    renderer.StencilPass = GL_DECR;
                    break;
            }

            renderer.GetMaterial().Value.Shader.Value.Use();
            renderer.GetMaterial().Value.Shader.Value.SetMatrix4x4("model",
                !element.UseWorldPosition
                    ? Matrix4x4.CreateScale(element.Size.X, element.Size.Y, 1f) * Matrix4x4.CreateTranslation(pos.X, pos.Y, 0f)
                    : Matrix4x4.CreateScale(element.Size.X, element.Size.Y, 1f) * Matrix4x4.CreateTranslation(pos.X, -pos.Y, 0f)
                        * Matrix4x4.CreateReflection(new Plane(0, 1, 0, pos.Y)));
            renderer.GetMaterial().Value.Shader.Value.SetMatrix4x4("uiMatrix",
                !element.UseWorldPosition ? UIModelMatrix : Matrix4x4.Identity);

            renderer.Render();
        }

        /// <summary>
        /// Called when a <see cref="UIElement"/> is parented to another element, removing it from the root list.
        /// </summary>
        /// <param name="element">The element that was parented.</param>
        public void OnElementParented(UIElement element)
        {
            _rootElements.Remove(element);
        }

        /// <summary>
        /// Called when a <see cref="UIElement"/> is unparented, adding it back to the root list.
        /// </summary>
        /// <param name="element">The element that was unparented.</param>
        public void OnElementUnparented(UIElement element)
        {
            if (!_rootElements.Contains(element))
                _rootElements.Add(element);
        }

        private void OnWindowResized()
        {
            UpdateScaling();

            foreach (var root in _rootElements)
            {
                root.InvalidateMeasure();
            }
        }

        private void UpdateScaling()
        {
            if (_scalingMode == UIScalingMode.RealResolution)
            {
                UIModelMatrix = Matrix4x4.Identity;
                UIModelMatrixInverse = Matrix4x4.Identity;
                VirtualResolution = Display.WindowSize;
            }
            else
            {
                VirtualResolution = ProjectSettings.UICanvasSettings.VirtualResolution;
                float scaleX = Display.WindowSize.X / VirtualResolution.X;
                float scaleY = Display.WindowSize.Y / VirtualResolution.Y;

                if (_maintainAspect)
                {
                    float uniformScale = MathF.Min(scaleX, scaleY);
                    scaleX = scaleY = uniformScale;
                }

                float offsetX = (Display.WindowSize.X - VirtualResolution.X * scaleX) / 2f;
                float offsetY = (Display.WindowSize.Y - VirtualResolution.Y * scaleY) / 2f;

                UIModelMatrix = Matrix4x4.CreateScale(scaleX, scaleY, 1f) *
                    Matrix4x4.CreateTranslation(offsetX, offsetY, 0f);

                Matrix4x4.Invert(UIModelMatrix, out var inverse);
                UIModelMatrixInverse = inverse;
            }

            OnUIScaleChanged?.Invoke(Scale);
            Resources.ReleaseUnusedFonts();
        }

        /// <summary>
        /// Converts a position from virtual space to screen space.
        /// </summary>
        /// <param name="position">The position in virtual space.</param>
        /// <returns>The position in screen space.</returns>
        public Vector2 VirtualToScreen(Vector2 position)
        {
            Vector4 r = Vector4.Transform(new Vector4(position, 0, 1), UIModelMatrix);
            return new Vector2(r.X, r.Y);
        }

        /// <summary>
        /// Converts a position from screen space to virtual space.
        /// </summary>
        /// <param name="position">The position in screen space.</param>
        /// <returns>The position in virtual space.</returns>
        public Vector2 ScreenToVirtual(Vector2 position)
        {
            Vector4 r = Vector4.Transform(new Vector4(position, 0, 1), UIModelMatrixInverse);
            return new Vector2(r.X, r.Y);
        }

        /// <summary>
        /// Converts a position from screen space to world space.
        /// </summary>
        /// <param name="position">The position in screen space.</param>
        /// <returns>The position in world space.</returns>
        public Vector2 ScreenToWorld(Vector2 position)
        {
            return CameraNode.Main.ScreenToWorld(position);
        }

        internal void UpdateLayout()
        {
            Vector2 canvasSize = ScreenToVirtual(Display.WindowSize);
            foreach (var root in _rootElements)
            {
                if (!root.IsMeasureValid)
                {
                    root.Measure(canvasSize);
                }

                if (!root.IsArrangeValid)
                {
                    root.Arrange(new Rect(0, 0, root.DesiredSize.X, root.DesiredSize.Y));
                }
            }

            (_arrangeQueue, _arrangeQueueBuffer) = (_arrangeQueueBuffer, _arrangeQueue);
            foreach (var element in _arrangeQueueBuffer)
            {
                element.ArrangeFromLastRect();
            }
            _arrangeQueueBuffer.Clear();
        }

        internal void QueueArrange(UIElement element)
        {
            _arrangeQueue.Add(element);
        }

        private void Update()
        {
            ProcessInput();
            UpdateLayout();
        }

        private void ProcessInput()
        {
            Vector2 mousePos = Input.GetMouseScreenPosition();
            Vector2 mousePosVirtual = ScreenToVirtual(mousePos);
            Vector2 mouseDelta = mousePosVirtual - _lastMousePosition;
            _lastMousePosition = mousePosVirtual;

            UIElement hitElement = HitTestTopMost(mousePosVirtual);

            if (hitElement != _hoveredElement)
            {
                if (_hoveredElement != null)
                {
                    var leaveEvt = new UIEvent
                    {
                        Type = UIEventType.MouseLeave,
                        Target = _hoveredElement,
                        Phase = EventPhase.Target,
                        MousePosition = mousePosVirtual
                    };
                    _hoveredElement.RaiseEvent(leaveEvt);
                }

                if (hitElement != null)
                {
                    var enterEvt = new UIEvent
                    {
                        Type = UIEventType.MouseEnter,
                        Target = hitElement,
                        Phase = EventPhase.Target,
                        MousePosition = mousePosVirtual
                    };
                    hitElement.RaiseEvent(enterEvt);
                }

                _hoveredElement = hitElement;
            }

            if (Input.ScrollDelta != 0 && hitElement != null)
            {
                var scrollEvt = new UIEvent
                {
                    Type = UIEventType.MouseScroll,
                    Target = hitElement,
                    Phase = EventPhase.Bubble,
                    MousePosition = mousePosVirtual,
                    MouseScrollDelta = Input.ScrollDelta
                };
                hitElement.RaiseEvent(scrollEvt);
            }

            if (_draggedElement != null)
            {
                var dragEvt = new UIEvent
                {
                    Type = UIEventType.Drag,
                    Target = _draggedElement,
                    Phase = EventPhase.Target,
                    MousePosition = mousePosVirtual,
                    MouseDelta = mouseDelta
                };
                _draggedElement.RaiseEvent(dragEvt);

                if (Input.GetMouseButtonUp(MouseButton.Left))
                {
                    var upEvt = new UIEvent
                    {
                        Type = UIEventType.MouseUp,
                        Target = _draggedElement,
                        Phase = EventPhase.Bubble,
                        MousePosition = mousePosVirtual,
                        MouseButton = MouseButton.Left
                    };
                    _draggedElement.RaiseEvent(upEvt);

                    var dragEndEvt = new UIEvent
                    {
                        Type = UIEventType.DragEnd,
                        Target = _draggedElement,
                        Phase = EventPhase.Target,
                        MousePosition = mousePosVirtual
                    };
                    _draggedElement.RaiseEvent(dragEndEvt);

                    if (hitElement == _draggedElement)
                    {
                        var clickEvt = new UIEvent
                        {
                            Type = UIEventType.Click,
                            Target = _draggedElement,
                            Phase = EventPhase.Bubble,
                            MousePosition = mousePosVirtual,
                            MouseButton = MouseButton.Left
                        };
                        _draggedElement.RaiseEvent(clickEvt);
                    }

                    _draggedElement = null;
                }
            }
            else
            {
                if (hitElement != null)
                {
                    if (Input.GetMouseButtonDown(MouseButton.Left))
                    {
                        var downEvt = new UIEvent
                        {
                            Type = UIEventType.MouseDown,
                            Target = hitElement,
                            Phase = EventPhase.Bubble,
                            MousePosition = mousePosVirtual,
                            MouseButton = MouseButton.Left
                        };
                        hitElement.RaiseEvent(downEvt);

                        var dragStartEvt = new UIEvent
                        {
                            Type = UIEventType.DragStart,
                            Target = hitElement,
                            Phase = EventPhase.Target,
                            MousePosition = mousePosVirtual
                        };
                        hitElement.RaiseEvent(dragStartEvt);

                        _draggedElement = hitElement;

                        if (_focusedElement != hitElement)
                        {
                            _focusedElement?.RaiseEvent(new UIEvent
                            {
                                Type = UIEventType.LoseFocus,
                                Target = _focusedElement,
                                Phase = EventPhase.Target
                            });
                            _focusedElement = hitElement;
                            hitElement.RaiseEvent(new UIEvent
                            {
                                Type = UIEventType.GainFocus,
                                Target = hitElement,
                                Phase = EventPhase.Target
                            });
                        }
                    }

                    if (Input.GetMouseButtonDown(MouseButton.Right))
                    {
                        var downEvt = new UIEvent
                        {
                            Type = UIEventType.MouseDown,
                            Target = hitElement,
                            Phase = EventPhase.Bubble,
                            MousePosition = mousePosVirtual,
                            MouseButton = MouseButton.Right
                        };
                        hitElement.RaiseEvent(downEvt);
                    }

                    if (Input.GetMouseButtonUp(MouseButton.Right))
                    {
                        var upEvt = new UIEvent
                        {
                            Type = UIEventType.MouseUp,
                            Target = hitElement,
                            Phase = EventPhase.Bubble,
                            MousePosition = mousePosVirtual,
                            MouseButton = MouseButton.Right
                        };
                        hitElement.RaiseEvent(upEvt);

                        var clickEvt = new UIEvent
                        {
                            Type = UIEventType.Click,
                            Target = hitElement,
                            Phase = EventPhase.Bubble,
                            MousePosition = mousePosVirtual,
                            MouseButton = MouseButton.Right
                        };
                        hitElement.RaiseEvent(clickEvt);
                    }

                    if (Input.GetMouseButtonDown(MouseButton.Middle))
                    {
                        var downEvt = new UIEvent
                        {
                            Type = UIEventType.MouseDown,
                            Target = hitElement,
                            Phase = EventPhase.Bubble,
                            MousePosition = mousePosVirtual,
                            MouseButton = MouseButton.Middle
                        };
                        hitElement.RaiseEvent(downEvt);
                    }

                    if (Input.GetMouseButtonUp(MouseButton.Middle))
                    {
                        var upEvt = new UIEvent
                        {
                            Type = UIEventType.MouseUp,
                            Target = hitElement,
                            Phase = EventPhase.Bubble,
                            MousePosition = mousePosVirtual,
                            MouseButton = MouseButton.Middle
                        };
                        hitElement.RaiseEvent(upEvt);

                        var clickEvt = new UIEvent
                        {
                            Type = UIEventType.Click,
                            Target = hitElement,
                            Phase = EventPhase.Bubble,
                            MousePosition = mousePosVirtual,
                            MouseButton = MouseButton.Middle
                        };
                        hitElement.RaiseEvent(clickEvt);
                    }
                }
                else
                {
                    if (Input.GetMouseButtonDown(MouseButton.Left)
                        || Input.GetMouseButtonDown(MouseButton.Right)
                        || Input.GetMouseButtonDown(MouseButton.Middle))
                    {
                        if (_focusedElement != null)
                        {
                            _focusedElement.RaiseEvent(new UIEvent
                            {
                                Type = UIEventType.LoseFocus,
                                Target = _focusedElement,
                                Phase = EventPhase.Target
                            });
                            _focusedElement = null;
                        }
                    }
                }
            }
        }

        private UIElement? HitTestTopMost(Vector2 mousePos)
        {
            // Test all root elements and their children (in reverse order, last = top)
            for (int i = _rootElements.Count - 1; i >= 0; i--)
            {
                UIElement element = _rootElements[i];
                Vector2 pos;
                if(element.UseWorldPosition)
                {
                    pos = ScreenToWorld(VirtualToScreen(mousePos));
                    pos.Y = -pos.Y;
                }
                else
                {
                    pos = mousePos;
                }
                var hit = HitTestRecursive(element, pos);
                if (hit != null) return hit;
            }
            return null;
        }

        private UIElement? HitTestRecursive(UIElement element, Vector2 position)
        {
            if (!element.Visible || !element.Enabled) return null;

            bool hitSelf = element.HitTest(position);

            if (!element.Mask || hitSelf)
            {
                for (int i = element.Children.Count - 1; i >= 0; i--)
                {
                    var hit = HitTestRecursive(element.Children[i], position);
                    if (hit != null) return hit;
                }
            }

            if (element.Interactable && hitSelf)
                return element;

            return null;
        }

        /// <summary>
        /// Sets focus to the given <see cref="UIElement"/>, removing focus from the previously focused element.
        /// </summary>
        /// <param name="element">The element to focus.</param>
        public void Focus(UIElement element)
        {
            if (_focusedElement == element) return;

            _focusedElement?.RaiseEvent(new UIEvent
            {
                Type = UIEventType.LoseFocus,
                Target = _focusedElement,
                Phase = EventPhase.Target
            });

            _focusedElement = element;
            element.Focused = true;
        }

        /// <summary>
        /// Removes focus from the given <see cref="UIElement"/> if it is currently focused.
        /// </summary>
        /// <param name="element">The element to unfocus.</param>
        public void Unfocus(UIElement element)
        {
            if (element != _focusedElement) return;

            element.Focused = false;
            _focusedElement = null;
        }
    }
}