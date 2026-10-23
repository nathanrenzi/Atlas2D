using Atlas2D.Rendering.Shaders;

using static Atlas2D.OpenGL.GL;

namespace Atlas2D.Rendering
{
    public class Material : IDisposable
    {
        private static SharedResource<Texture2D> _blankTexture = null;
        private static SharedResource<Texture2D> _blankNormal = null;

        public SharedResource<Shader> Shader { get; private set; }
        public SharedResource<Texture2D> MainTexture { get; private set; }
        public SharedResource<Texture2D> NormalTexture { get; private set; }
        public SharedResource<Texture2DArray> MainTextureArray { get; private set; }
        public SharedResource<Texture2DArray> NormalTextureArray { get; private set; }
        public float NormalScale { get; set; }
        public Color MainColor { get; set; }
        public bool UseLinearFiltering { get; }
        public bool UseTextureArrays { get; }

        private Material(SharedResource<Shader> shader, SharedResource<Texture2D> mainTexture, SharedResource<Texture2D> normalTexture,
            Color mainColor, bool useLinearFiltering, float normalScale)
        {
            Shader = shader.AddRef();
            MainTexture = mainTexture.AddRef();
            NormalTexture = normalTexture.AddRef();
            MainColor = mainColor;
            UseLinearFiltering = useLinearFiltering;
            NormalScale = normalScale;
            PrepareShader(shader);
        }

        private Material(SharedResource<Shader> shader, SharedResource<Texture2DArray> mainTextureArray,
            SharedResource<Texture2DArray> normalTextureArray, Color mainColor, bool useLinearFiltering, float normalScale)
        {
            if (mainTextureArray == null)
                throw new ArgumentNullException(nameof(mainTextureArray), "A texture array material needs a main texture array.");

            Shader = shader.AddRef();
            MainTextureArray = mainTextureArray.AddRef();
            NormalTextureArray = normalTextureArray?.AddRef();
            // Code that only knows about regular textures reads these, so they draw blank instead of crashing
            MainTexture = _blankTexture.AddRef();
            NormalTexture = _blankNormal.AddRef();
            MainColor = mainColor;
            UseLinearFiltering = useLinearFiltering;
            UseTextureArrays = true;
            NormalScale = normalScale;
            PrepareShader(shader);
        }

        private static void PrepareShader(SharedResource<Shader> shader)
        {
            if (shader.Value.Compiled) return;
            shader.Value.Compile();
            shader.Value.Use();
            shader.Value.SetInt("mainTextureSampler", 0);
            shader.Value.SetInt("normalTextureSampler", 1);
        }

        public void SetShader(SharedResource<Shader> shader)
        {
            var previous = Shader;
            Shader = shader.AddRef();
            previous.Release();
            PrepareShader(Shader);
        }

        public void SetMainTexture(SharedResource<Texture2D> mainTexture)
        {
            if(UseTextureArrays)
            {
                Debug.LogError("Cannot set a Texture2D on a material that uses a Texture2DArray. Create a new material with Material.Create instead.");
                return;
            }
            var previous = MainTexture;
            MainTexture = (mainTexture ?? _blankTexture).AddRef();
            previous.Release();
        }

        public void SetNormalTexture(SharedResource<Texture2D> normalTexture)
        {
            if (UseTextureArrays)
            {
                Debug.LogError("Cannot set a Texture2D on a material that uses a Texture2DArray. Create a new material with Material.Create instead.");
                return;
            }
            var previous = NormalTexture;
            NormalTexture = (normalTexture ?? _blankNormal).AddRef();
            previous.Release();
        }

        public void SetMainTextureArray(SharedResource<Texture2DArray> mainTextureArray)
        {
            if (!UseTextureArrays)
            {
                Debug.LogError("Cannot set a Texture2DArray on a material that uses a Texture2D. Create a new material with Material.CreateArray instead.");
                return;
            }
            if (mainTextureArray == null)
            {
                Debug.LogError("Cannot set the main texture array of a material to null.");
                return;
            }
            var previous = MainTextureArray;
            MainTextureArray = mainTextureArray.AddRef();
            previous.Release();
        }

        public void SetNormalTextureArray(SharedResource<Texture2DArray> normalTextureArray)
        {
            if (!UseTextureArrays)
            {
                Debug.LogError("Cannot set a Texture2DArray on a material that uses a Texture2D. Create a new material with Material.CreateArray instead.");
                return;
            }
            var previous = NormalTextureArray;
            NormalTextureArray = normalTextureArray?.AddRef();
            previous?.Release();
        }

        public void Dispose()
        {
            Shader.Release();
            MainTexture.Release();
            NormalTexture.Release();
            MainTextureArray?.Release();
            NormalTextureArray?.Release();
        }

        internal static void Initialize()
        {
            _blankTexture ??= Resources.GetTexture(Resources.GetEngineResourcePath("Textures/BlankTexture.png"));
            _blankNormal ??= Resources.GetTexture(Resources.GetEngineResourcePath("Textures/BlankNormal.png"), true);
        }

        internal static void Shutdown()
        {
            _blankTexture.Release();
            _blankNormal.Release();
        }

        #region Static Methods
        public static Material Create(Color color) => Create(GlobalShaders.Texture, color);

        public static Material Create(SharedResource<Texture2D> mainTexture = null, SharedResource<Texture2D> normalTexture = null,
            bool useLinearFiltering = false, float normalScale = 1)
            => Create(GlobalShaders.Texture, Color.White, mainTexture, normalTexture, useLinearFiltering, normalScale);

        public static Material Create(SharedResource<Shader> shader, SharedResource<Texture2D> mainTexture = null,
            SharedResource<Texture2D> normalTexture = null, bool useLinearFiltering = false, float normalScale = 1)
            => Create(shader, Color.White, mainTexture, normalTexture, useLinearFiltering, normalScale);

        public static Material Create(SharedResource<Shader> shader, Color mainColor, SharedResource<Texture2D> mainTexture = null,
            SharedResource<Texture2D> normalTexture = null, bool useLinearFiltering = false, float normalScale = 1)
        {
            return new Material(shader, mainTexture ?? _blankTexture, normalTexture ?? _blankNormal,
                mainColor, useLinearFiltering, normalScale);
        }

        public static Material CreateArray(SharedResource<Texture2DArray> mainTextureArray, SharedResource<Texture2DArray> normalTextureArray = null,
            bool useLinearFiltering = false, float normalScale = 1)
            => CreateArray(GlobalShaders.TextureArray, Color.White, mainTextureArray, normalTextureArray, useLinearFiltering, normalScale);

        public static Material CreateArray(SharedResource<Shader> shader, SharedResource<Texture2DArray> mainTextureArray,
            SharedResource<Texture2DArray> normalTextureArray = null, bool useLinearFiltering = false, float normalScale = 1)
            => CreateArray(shader, Color.White, mainTextureArray, normalTextureArray, useLinearFiltering, normalScale);

        public static Material CreateArray(SharedResource<Shader> shader, Color mainColor, SharedResource<Texture2DArray> mainTextureArray,
            SharedResource<Texture2DArray> normalTextureArray = null, bool useLinearFiltering = false, float normalScale = 1)
        {
            return new Material(shader, mainTextureArray, normalTextureArray,
                mainColor, useLinearFiltering, normalScale);
        }

        public static Material CreateCircle(SharedResource<Shader> shader, Color color)
        {
            SharedResource<Texture2D> tex = Resources.GetTexture(Resources.GetEngineResourcePath("Textures/Circle.png"));
            Material mat = Create(shader, color, tex);
            tex.Release();
            return mat;
        }

        public static Material CreateCircle(Color color)
        {
            return CreateCircle(GlobalShaders.Texture, color);
        }

        public static Material CreateCircle(Color color, bool forInterface = false)
        {
            return CreateCircle(forInterface ? GlobalShaders.Interface : GlobalShaders.Texture, color);
        }

        public static Material CreateLit(Color mainColor, SharedResource<Texture2D> mainTexture = null,
            SharedResource<Texture2D> normalTexture = null, bool useLinearFiltering = false, float normalScale = 1)
        {
            return Create(GlobalShaders.Lit, mainColor, mainTexture, normalTexture, useLinearFiltering, normalScale);
        }

        public static Material CreateLit(SharedResource<Texture2D> mainTexture, SharedResource<Texture2D> normalTexture = null,
            bool useLinearFiltering = false, float normalScale = 1)
        {
            return Create(GlobalShaders.Lit, Color.White, mainTexture, normalTexture, useLinearFiltering, normalScale);
        }

        public static Material CreateLitArray(Color mainColor, SharedResource<Texture2DArray> mainTextureArray,
            SharedResource<Texture2DArray> normalTextureArray = null, bool useLinearFiltering = false, float normalScale = 1)
        {
            return CreateArray(GlobalShaders.LitArray, mainColor, mainTextureArray, normalTextureArray, useLinearFiltering, normalScale);
        }

        public static Material CreateLitArray(SharedResource<Texture2DArray> mainTextureArray,
            SharedResource<Texture2DArray> normalTextureArray = null, bool useLinearFiltering = false, float normalScale = 1)
        {
            return CreateArray(GlobalShaders.LitArray, Color.White, mainTextureArray, normalTextureArray, useLinearFiltering, normalScale);
        }

        public static Material Copy(Material material)
        {
            if (material.UseTextureArrays)
            {
                return new Material(material.Shader, material.MainTextureArray, material.NormalTextureArray,
                    material.MainColor, material.UseLinearFiltering, material.NormalScale);
            }
            else
            {
                return new Material(material.Shader, material.MainTexture, material.NormalTexture,
                    material.MainColor, material.UseLinearFiltering, material.NormalScale);
            }
        }
        #endregion

        public void Use()
        {
            if(UseTextureArrays)
            {
                MainTextureArray.Value.SetFilteringMode(UseLinearFiltering);
                MainTextureArray.Value.Use(GL_TEXTURE0);
                if (NormalTextureArray != null)
                {
                    NormalTextureArray.Value.SetFilteringMode(UseLinearFiltering);
                    NormalTextureArray.Value.Use(GL_TEXTURE1);
                }
            }
            else
            {
                MainTexture.Value.SetFilteringMode(UseLinearFiltering);
                NormalTexture.Value.SetFilteringMode(UseLinearFiltering);
                MainTexture.Value.Use(GL_TEXTURE0);
                NormalTexture.Value.Use(GL_TEXTURE1);
            }

            Shader.Value.Use();
            Shader.Value.SetInt("mainTextureSampler", 0);
            Shader.Value.SetInt("normalTextureSampler", 1);
            Shader.Value.SetInt("hasNormalTexture", UseTextureArrays && NormalTextureArray == null ? 0 : 1);
            Shader.Value.SetColor("mainColor", MainColor);
            Shader.Value.SetFloat("normalScale", NormalScale);
        }
    }
}