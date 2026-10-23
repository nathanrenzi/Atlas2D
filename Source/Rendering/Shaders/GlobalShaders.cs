namespace Atlas2D.Rendering.Shaders
{
    public static class GlobalShaders
    {
        public static SharedResource<Shader> TexturedVertex { get; private set; }
        public static SharedResource<Shader> Texture { get; private set; }
        public static SharedResource<Shader> TextureArray { get; private set; }
        public static SharedResource<Shader> Interface { get; private set; }
        public static SharedResource<Shader> Vertex { get; private set; }
        public static SharedResource<Shader> Text { get; private set; }
        public static SharedResource<Shader> Lit { get; private set; }
        public static SharedResource<Shader> LitArray { get; private set; }
        public static SharedResource<Shader> StencilOnly { get; private set; }

        internal static void Initialize()
        {
            TexturedVertex = SharedResource<Shader>.Create(new Shader(Shader.ParseShader(Resources.GetEngineResourcePath("Shaders/TexturedVertex.glsl")), true));
            Texture = SharedResource<Shader>.Create(new Shader(Shader.ParseShader(Resources.GetEngineResourcePath("Shaders/Texture.glsl")), true));
            TextureArray = SharedResource<Shader>.Create(new Shader(Shader.ParseShader(Resources.GetEngineResourcePath("Shaders/TextureArray.glsl")), true));
            Interface = SharedResource<Shader>.Create(new Shader(Shader.ParseShader(Resources.GetEngineResourcePath("Shaders/Interface.glsl")), true));
            Vertex = SharedResource<Shader>.Create(new Shader(Shader.ParseShader(Resources.GetEngineResourcePath("Shaders/Vertex.glsl")), true));
            Text = SharedResource<Shader>.Create(new Shader(Shader.ParseShader(Resources.GetEngineResourcePath("Shaders/Text.glsl")), true));
            Lit = SharedResource<Shader>.Create(new Shader(Shader.ParseShader(Resources.GetEngineResourcePath("Shaders/Lit.glsl")), true, new string[] { "lights" }));
            LitArray = SharedResource<Shader>.Create(new Shader(Shader.ParseShader(Resources.GetEngineResourcePath("Shaders/LitArray.glsl")), true, new string[] { "lights" }));
            StencilOnly = SharedResource<Shader>.Create(new Shader(Shader.ParseShader(Resources.GetEngineResourcePath("Shaders/StencilOnly.glsl")), true));
        }

        internal static void Shutdown()
        {
            TexturedVertex.Release();
            Texture.Release();
            TextureArray.Release();
            Interface.Release();
            Vertex.Release();
            Text.Release();
            Lit.Release();
            LitArray.Release();
            StencilOnly.Release();
        }
    }
}