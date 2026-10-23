using static Atlas2D.OpenGL.GL;

namespace Atlas2D.Rendering
{
    /// <summary>
    /// A multi-purpose mesh renderer.
    /// </summary>
    public class MeshRenderer : IGameClass
    {
        public float[] Vertices { get; protected set; }
        public uint[] Indices { get; protected set; }

        public BufferLayout Layout { get; protected set; }
        public VertexBuffer VertexBuffer { get; protected set; }
        public VertexArray VertexArray { get; protected set; }
        public IndexBuffer IndexBuffer { get; protected set; }
        public SharedResource<Material> Material { get; protected set; }
        public int RenderLayer { get; protected set; }
        public Action OnBeforeRender { get; set; }
        public Action OnAfterRender { get; set; }

        private TransformNode? _transform;
        private bool _disposed = false;

        /// <summary>
        /// If enabled, the object will not move in world space, but will instead stay in one place in screen space.
        /// </summary>
        public bool UseUnscaledProjectionMatrix { get; set; } = false;
        public bool UseCustomIndexRenderCount { get; set; } = false;
        public int CustomIndexRenderCount { get; set; } = 0;
        public bool HasVertexData { get; private set; } = false;
        public bool IsVertexDirty { get; set; } = false;
        public bool IsIndexDirty { get; set; } = false;
        public bool IsLoaded { get; set; } = false;

        public bool UseStencilBuffer { get; set; } = false;
        public bool StencilPreClearBuffer { get; set; } = false;
        public int StencilFail { get; set; } = GL_KEEP;
        public int StencilPass { get; set; } = GL_KEEP;
        public uint StencilMask { get; set; } = 0x00;
        public int StencilFunction { get; set; } = GL_ALWAYS;
        public int StencilReference { get; set; } = 1;
        public uint StencilFunctionMask { get; set; } = 0xFF;
        public bool Enabled { get; set; } = true;

        public MeshRenderer(SharedResource<Material> material)
        {
            _transform = null;
            Material = material.AddRef();
        }

        public MeshRenderer(TransformNode transform, SharedResource<Material> material)
        {
            _transform = transform;
            Material = material.AddRef();
        }

        public void Update() { }
        public void FixedUpdate() { }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if(IsLoaded)
            {
                VertexBuffer.Dispose();
                VertexArray.Dispose();
                IndexBuffer.Dispose();
            }
            Material.Release();
        }

        #region Materials
        public void SetMaterial(SharedResource<Material> material)
        {
            Material.Release();
            Material = material.AddRef();
        }

        public SharedResource<Material> GetMaterial() => Material;
        #endregion

        #region Vertex Manipulation

        /// <summary>
        /// This must be called to initialize the renderer.
        /// </summary>
        /// <param name="_vertices"></param>
        /// <param name="_indices"></param>
        /// <param name="_loadOnSetArrays">Should the renderer be loaded when the arrays have been set (Should usually be left as true).</param>
        public void SetVertexArrays(float[] _vertices, uint[] _indices, bool _loadOnSetArrays = true, bool _setDirty = false)
        {
            Vertices = _vertices;
            Indices = _indices;

            HasVertexData = true;
            if (_loadOnSetArrays) Load();
            if (_setDirty) IsVertexDirty = true;
            if (_setDirty) IsIndexDirty = true;
        }

        /// <summary>
        /// Sets a specific value in every vertex.
        /// </summary>
        /// <param name="_type">The type of attribute to edit.</param>
        /// <param name="_value">The value to set.</param>
        public void SetVertexValueAll(int _type, float _value)
        {
            if (!HasVertexData) return;
            int loops = Vertices.Length / Layout.GetRawStride();

            // Setting the value for each vertex
            for (int i = 0; i < loops; i++)
            {
                Vertices[(i * Layout.GetRawStride()) + _type] = _value;
            }

            IsVertexDirty = true;
        }

        /// <summary>
        /// Returns the vertex value of the specified type. Samples from the first vertex by default.
        /// </summary>
        /// <param name="_type">The type of vertex data to return.</param>
        /// <returns></returns>
        public float GetVertexValue(int _type, int _vertex = 0)
        {
            return Vertices[(_vertex * Layout.GetRawStride()) + _type];
        }

        #endregion

        /// <summary>
        /// Loads all resources necessary for the renderer, such as the shader and buffers.
        /// </summary>
        public void Load(bool createBufferLayoutOnLoad = true)
        {
            if (!HasVertexData) return;

            VertexArray = new VertexArray();
            VertexBuffer = new VertexBuffer(Vertices);
            IndexBuffer = new IndexBuffer(Indices);

            if(createBufferLayoutOnLoad)
            {
                CreateBufferLayout();
            }          

            VertexArray.AddBuffer(VertexBuffer, Layout);

            IsLoaded = true;
        }

        protected virtual void CreateBufferLayout()
        {
            // Telling the vertex array how the vertices are structured
            Layout = new BufferLayout();
            Layout.Add<float>(2); // Position
            Layout.Add<float>(2); // UV
        }

        public void SetBufferLayoutBeforeLoad(BufferLayout layout)
        {
            if (IsLoaded) return;
            Layout = layout;
        }

        public unsafe void Render()
        {
            if (!Enabled) return;
            if (!HasVertexData) return;
            if (!IsLoaded || Material.Value.Shader.Value.Compiled == false) return;

            if (IsVertexDirty)
            {
                // Updating the data inside of the vertex buffer
                VertexBuffer.UpdateData(Vertices);
                IsVertexDirty = false;
            }

            if (IsIndexDirty)
            {
                // Updating the data inside of the index buffer
                IndexBuffer.UpdateData(Indices);
                IsIndexDirty = false;
            }

            if (UseStencilBuffer)
            {
                if (StencilPreClearBuffer)
                    glClear(GL_STENCIL_BUFFER_BIT);

                glStencilOp(StencilFail, GL_KEEP, StencilPass);
                glStencilFunc(StencilFunction, StencilReference, StencilFunctionMask);
                glStencilMask(StencilMask);
            }
            else
            {
                glStencilOp(GL_KEEP, GL_KEEP, GL_KEEP);
                glStencilFunc(GL_ALWAYS, 0, 0xFF);
                glStencilMask(0x00);
            }

            Material.Value.Use();
            if(_transform != null)
            {
                Material.Value.Shader.Value.SetMatrix4x4("model", _transform.WorldMatrix);
            }
            Material.Value.Shader.Value.SetMatrix4x4("projection", UseUnscaledProjectionMatrix ? CameraNode.Main.GetUnscaledProjectionMatrix() : CameraNode.Main.GetViewProjectionMatrix());

            VertexArray.Bind();
            IndexBuffer.Bind();

            BeforeRender();
            OnBeforeRender?.Invoke();
            glDrawElements(GL_TRIANGLES, UseCustomIndexRenderCount ? CustomIndexRenderCount : Indices.Length, GL_UNSIGNED_INT, (void*)0);
            OnAfterRender?.Invoke();
        }

        protected virtual void BeforeRender() { }
    }

    /// <summary>
    /// This enum corresponds to an attribute in a vertex for the renderer.
    /// </summary>
    public enum MeshVertexAttribute
    {
        PositionX = 0,
        PositionY = 1,
        UvX = 2,
        UvY = 3,
    }
}