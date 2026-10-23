using Atlas2D.Management;
using Atlas2D.Rendering;
using Atlas2D.Rendering.Text;
using Atlas2D.UI;
using System.Text;

namespace Atlas2D
{
    public static class Resources
    {
        private static Dictionary<string, SharedResource<Texture2DArray>> textureArrayCache = new();
        private static Dictionary<string, SharedResource<Texture2D>> textureCache = new();
        private static Dictionary<uint, SharedResource<Texture2D>> textureHandleCache = new();
        private static Dictionary<(FontArgs Args, float UIScale), SharedResource<FontGlyphStore>> fontCache = new();
        private static SharedResource<Texture2D> missingTexture;

        internal static void Shutdown()
        {
            missingTexture?.Release();
            missingTexture = null;
            foreach (var handle in textureCache.Values) handle.Release();
            foreach (var handle in textureArrayCache.Values) handle.Release();
            foreach (var handle in fontCache.Values) handle.Release();
            textureCache.Clear();
            textureArrayCache.Clear();
            textureHandleCache.Clear();
            fontCache.Clear();
        }

        private static bool ValidatePath(string path)
        {
            if (!File.Exists(path))
            {
                Debug.LogError($"File not found: {path}");
                return false;
            }
            return true;
        }

        public static string GetEngineResourcePath(string localPath)
        {
            return Path.Combine(ProjectSettings.EngineResourcePath, localPath);
        }

        #region Texture Arrays
        public static SharedResource<Texture2DArray> GetTextureArray(string path, int layers, bool nonColor = false)
        {
            if (!ValidatePath(path)) return null;
            if (!textureArrayCache.TryGetValue(path, out var handle))
            {
                handle = SharedResource<Texture2DArray>.Create(TextureFactory.LoadArray(path, layers, nonColor));
                textureArrayCache.Add(path, handle);
            }
            return handle.AddRef();
        }

        public static SharedResource<Texture2DArray> GetTextureArray(string[] paths, bool nonColor = false)
        {
            StringBuilder builder = new();
            foreach (var path in paths)
            {
                if (!ValidatePath(path)) return null;
                builder.Append(path);
            }

            string key = builder.ToString();
            if (!textureArrayCache.TryGetValue(key, out var handle))
            {
                handle = SharedResource<Texture2DArray>.Create(TextureFactory.LoadArray(paths, nonColor));
                textureArrayCache.Add(key, handle);
            }
            return handle.AddRef();
        }

        public static SharedResource<Texture2DArray> GetTextureArray(string path, int spriteWidth, int spriteHeight, bool nonColor = false)
        {
            if (!ValidatePath(path)) return null;
            if (!textureArrayCache.TryGetValue(path, out var handle))
            {
                handle = SharedResource<Texture2DArray>.Create(TextureFactory.LoadArray(path, spriteWidth, spriteHeight, nonColor));
                textureArrayCache.Add(path, handle);
            }
            return handle.AddRef();
        }
        #endregion

        #region Textures
        /// <summary>
        /// Gets a cached texture. If the file is missing or can't be read, a magenta and black placeholder is returned instead.
        /// </summary>
        public static SharedResource<Texture2D> GetTexture(string path, bool nonColor = false)
        {
            if (!textureCache.TryGetValue(path, out var handle))
            {
                if (!ValidatePath(path)) return GetMissingTexture();

                Texture2D texture;
                try
                {
                    texture = TextureFactory.Load(path, nonColor);
                }
                catch (Exception e)
                {
                    Debug.LogError($"Could not load texture {path}: {e.Message}");
                    return GetMissingTexture();
                }

                handle = SharedResource<Texture2D>.Create(texture);
                textureCache.Add(path, handle);
                textureHandleCache.Add(texture.Handle, handle);
            }
            return handle.AddRef();
        }

        private static SharedResource<Texture2D> GetMissingTexture()
        {
            if (missingTexture == null)
            {
                Texture2D texture = TextureFactory.CreateMissingTexture();
                missingTexture = SharedResource<Texture2D>.Create(texture);
                textureHandleCache.Add(texture.Handle, missingTexture);
            }
            return missingTexture.AddRef();
        }

        public static SharedResource<Texture2D> GetTexture(uint handle)
        {
            if (textureHandleCache.TryGetValue(handle, out var resource))
                return resource.AddRef();

            throw new InvalidOperationException($"Texture with handle {handle} does not exist.");
        }
        #endregion

        #region Fonts
        /// <summary>
        /// Gets a font rendered for the current UI scale.
        /// </summary>
        public static SharedResource<FontGlyphStore> GetFont(string path, int fontSize, float fontScale, int outlineSize)
        {
            if (!ValidatePath(path)) return null;

            FontArgs args = new FontArgs()
            {
                FontFile = Path.GetFullPath(path),
                FontSize = fontSize,
                FontScale = fontScale,
                OutlineWidth = outlineSize
            };
            var key = (args, UICanvas.Instance.Scale);

            if (!fontCache.TryGetValue(key, out var handle))
            {
                handle = SharedResource<FontGlyphStore>.Create(FontGlyphFactory.Load(path, fontSize, fontScale, outlineSize));
                fontCache.Add(key, handle);
            }
            return handle.AddRef();
        }

        /// <summary>
        /// Frees fonts that only the cache is still holding, such as ones rendered for an old UI scale.
        /// </summary>
        internal static void ReleaseUnusedFonts()
        {
            foreach (var entry in fontCache.Where(entry => entry.Value.RefCount == 1).ToList())
            {
                entry.Value.Release();
                fontCache.Remove(entry.Key);
            }
        }
        #endregion
    }
}