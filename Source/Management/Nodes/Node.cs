using Atlas2D.UI;

namespace Atlas2D
{
    /// <summary>
    /// The base class for all scene hierarchy elements.
    /// Handles grouping, lifecycle management, and parent-child state propagation for
    /// <see cref="IGameClass"/>, <see cref="UIComponent"/>, and nested <see cref="Node"/> instances.
    /// </summary>
    public class Node : IGameClass
    {
        protected struct UIState
        {
            public bool Visible;
            public bool Interactable;
        }

        public Node? Parent { get; private set; } = null;
        protected readonly List<IGameClass> _gameClasses = new List<IGameClass>();
        protected readonly List<UIElement> _uiElements = new List<UIElement>();

        protected bool _disposed = false;
        private bool _disposing = false;
        private bool _disabledByParent = false;
        private bool _desiredEnableState = true;
        private bool _isLoaded = false;
        protected bool _enabled = false;

        // Reused every frame so Update/FixedUpdate don't allocate
        private readonly List<IGameClass> _updateBuffer = new List<IGameClass>();

        public bool Enabled => _enabled && !_disabledByParent;

        public Node()
        {
            Engine.Game.RegisterGameClass(this);
            Load();
        }

        protected Node(bool load)
        {
            Engine.Game.RegisterGameClass(this);
            if(load)
            {
                Load();
            }
        }

        protected void Load()
        {
            if (_isLoaded) return;
            _isLoaded = true;
            OnLoad();
        }

        /// <summary>
        /// Should create any objects that should be added to the node. Root nodes (nodes with no
        /// parent) should call <see cref="Enable"/> once they're ready to become active.
        /// </summary>
        protected virtual void OnLoad() { }
        protected virtual void OnEnable() { }
        protected virtual void OnDisable() { }
        protected virtual void OnUpdate() { }
        protected virtual void OnFixedUpdate() { }
        protected virtual void OnDispose() { }
        protected virtual void OnChildAdded(IGameClass child) { }
        protected virtual void OnChildRemoved(IGameClass child) { }
        protected virtual void OnParentChanged(Node newParent) { }

        /// <summary>
        /// Called when this node's world transform may have changed because it or a parent moved.
        /// </summary>
        internal virtual void OnWorldTransformInvalidated()
        {
            for (int i = 0; i < _gameClasses.Count; i++)
            {
                if (_gameClasses[i] is Node node)
                {
                    node.OnWorldTransformInvalidated();
                }
            }
        }

        public bool Contains(IGameClass gameClass)
        {
            if (_disposed) return false;
            return _gameClasses.Contains(gameClass);
        }

        public bool Contains(UIElement element)
        {
            if (_disposed) return false;
            return _uiElements.Contains(element);
        }

        private bool ContainsRecursive(Node node)
        {
            foreach (var cls in _gameClasses)
            {
                if (cls == node) return true;
                if (cls is Node sg && sg.ContainsRecursive(node)) return true;
            }
            return false;
        }

        public void AddChild(IGameClass gameClass)
        {
            if (_disposed || gameClass == this || _gameClasses.Contains(gameClass)) return;

            if (gameClass is Node sg)
            {
                if (sg.Parent != null)
                {
                    sg.Parent.RemoveChild(sg);
                }

                if (sg.ContainsRecursive(this))
                {
                    throw new Exception("Circular dependency detected when trying to register Node.");
                }

                sg.SetParent(this);


                if (_enabled)
                {
                    sg._disabledByParent = false;
                    if (sg._desiredEnableState)
                    {
                        sg.EnableInternal(false);
                    }
                }
                else
                {
                    sg._disabledByParent = true;
                    sg.DisableInternal(true);
                }
            }

            Engine.Game.UnregisterGameClass(gameClass);
            _gameClasses.Add(gameClass);
            OnChildAdded(gameClass);
        }

        public void AddChild(UIElement element)
        {
            if (_disposed || _uiElements.Contains(element)) return;
            element.Enabled = _enabled;
            _uiElements.Add(element);
        }

        /// <summary>
        /// Removes a child. The child goes back to being updated by the game as a root.
        /// </summary>
        public void RemoveChild(IGameClass gameClass)
        {
            if (_disposed || !_gameClasses.Remove(gameClass)) return;
            OnChildRemoved(gameClass);

            if (gameClass is Node sg)
            {
                sg.SetParent(null);
                sg._disabledByParent = false;

                // Restore intended state if it was being suppressed by the old parent
                if (sg._desiredEnableState && !sg._enabled)
                {
                    sg.EnableInternal(false);
                }
            }

            Engine.Game.RegisterGameClass(gameClass);
        }

        public void RemoveChild(UIElement element)
        {
            if (_disposed) return;
            element.Enabled = true;
            _uiElements.Remove(element);
        }

        public void SetParent(Node parent)
        {
            if (_disposed) return;

            Parent = parent;
            OnWorldTransformInvalidated();
            OnParentChanged(parent);
        }

        /// <summary>
        /// Enables the node, and all registered objects.
        /// </summary>
        public void Enable()
        {
            EnableInternal(false);
        }

        private void EnableInternal(bool parentEnabling)
        {
            if (_disposed) return;

            if (!parentEnabling)
            {
                _desiredEnableState = true;
            }

            if (_enabled || _disabledByParent) return;
            _enabled = true;

            // Enable children
            foreach (var element in _uiElements)
            {
                element.Enabled = true;
                element.InvalidateMeasure();
            }
            if (_uiElements.Count > 0)
            {
                // Lays out re-enabled elements right away. The canvas doesn't exist before Load().
                UICanvas.Instance?.UpdateLayout();
            }
            foreach (var cls in _gameClasses)
            {
                if (cls is Node sg)
                {
                    sg._disabledByParent = false;
                    if (sg._desiredEnableState)
                    {
                        sg.EnableInternal(false);
                    }
                }
            }

            OnEnable();
        }

        /// <summary>
        /// Disables the node, and all registered objects.
        /// </summary>
        public void Disable()
        {
            DisableInternal(false);
        }

        private void DisableInternal(bool parentDisabling)
        {
            if (_disposed) return;

            if (parentDisabling)
            {
                _disabledByParent = true;
            }
            else
            {
                _desiredEnableState = false;
            }

            if (!_enabled) return;
            _enabled = false;

            foreach (var element in _uiElements)
            {
                element.Enabled = false;
            }
            foreach (var cls in _gameClasses)
            {
                if (cls is Node sg)
                {
                    sg.DisableInternal(true);
                }
            }

            OnDisable();
        }

        public void Update()
        {
            if (_disposed || !_enabled) return;

            // Children can add or remove children while updating, so loop over a copy
            _updateBuffer.AddRange(_gameClasses);
            for (int i = 0; i < _updateBuffer.Count; i++)
            {
                if (_disposed) break;
                IGameClass gameClass = _updateBuffer[i];
                // Skip nodes that were removed or disposed earlier this frame
                if (gameClass is Node node && (node.Parent != this || node._disposed)) continue;
                gameClass.Update();
            }
            _updateBuffer.Clear();

            if (_disposed) return;
            OnUpdate();
        }

        public void FixedUpdate()
        {
            if (_disposed || !_enabled) return;

            _updateBuffer.AddRange(_gameClasses);
            for (int i = 0; i < _updateBuffer.Count; i++)
            {
                if (_disposed) break;
                IGameClass gameClass = _updateBuffer[i];
                if (gameClass is Node node && (node.Parent != this || node._disposed)) continue;
                gameClass.FixedUpdate();
            }
            _updateBuffer.Clear();

            if (_disposed) return;
            OnFixedUpdate();
        }

        /// <summary>
        /// Disposes the node, and all registered objects.
        /// </summary>
        public void Dispose()
        {
            if (_disposed || _disposing) return;
            _disposing = true;

            // Runs OnDisable first, so nodes only need to clean up once
            DisableInternal(false);

            if (Parent != null)
            {
                Node parent = Parent;
                parent._gameClasses.Remove(this);
                parent.OnChildRemoved(this);
                Parent = null;
            }

            _disposed = true;

            IGameClass[] children = _gameClasses.ToArray();
            _gameClasses.Clear();
            foreach (var gameClass in children)
            {
                // Stops the child from trying to remove itself from this node
                if (gameClass is Node node) node.Parent = null;
                gameClass.Dispose();
            }

            UIElement[] elements = _uiElements.ToArray();
            _uiElements.Clear();
            foreach (var element in elements)
            {
                element.Dispose();
            }

            Engine.Game.UnregisterGameClass(this);
            OnDispose();
        }
    }
}
