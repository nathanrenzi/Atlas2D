using Atlas2D.Management;
using Atlas2D.Rendering;
using Atlas2D.Rendering.Text;
using Atlas2D.UI;

namespace Atlas2D
{
    /// <summary>
    /// Loads and caches textures, texture arrays and fonts. Every getter returns a new reference that the caller must release.
    /// </summary>
    public static class Resources
    {
        private static Dictionary<string, SharedResource<Texture2DArray>> textureArrayCache = new();
        private static Dictionary<string, SharedResource<Texture2D>> textureCache = new();
        private static Dictionary<uint, SharedResource<Texture2D>> textureHandleCache = new();
        private static Dictionary<(FontArgs Args, float UIScale), SharedResource<FontGlyphStore>> fontCache = new();
        private static SharedResource<Texture2D> missingTexture;
        private static SharedResource<Texture2DArray> missingTextureArray;

        internal static void Shutdown()
        {
            missingTexture?.Release();
            missingTexture = null;
            missingTextureArray?.Release();
            missingTextureArray = null;
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

        /// <summary>
        /// Gets the path of a file inside the engine's resource folder.
        /// </summary>
        public static string GetEngineResourcePath(string localPath)
        {
            return Path.Combine(ProjectSettings.EngineResourcePath, localPath);
        }

        #region Texture Arrays
        /// <summary>
        /// Gets a cached texture array from an image made of equally sized layers stacked vertically. Layer 0 is the top slice.
        /// If the file is missing or can't be split, a one layer magenta and black placeholder is returned instead.
        /// </summary>
        public static SharedResource<Texture2DArray> GetTextureArray(string path, int layers, bool nonColor = false)
            => GetTextureArray($"{path}:{layers}", new[] { path }, () => TextureFactory.LoadArray(path, layers, nonColor));

        /// <summary>
        /// Gets a cached texture array with one layer per image, in the order given. Every image must be the same size.
        /// If a file is missing or the sizes don't match, a one layer magenta and black placeholder is returned instead.
        /// </summary>
        public static SharedResource<Texture2DArray> GetTextureArray(string[] paths, bool nonColor = false)
            => GetTextureArray(string.Join("|", paths), paths, () => TextureFactory.LoadArray(paths, nonColor));

        /// <summary>
        /// Gets a cached texture array from a spritesheet, with one layer per sprite going left to right, then top to bottom.
        /// If the file is missing or can't be split into sprites of that size, a one layer magenta and black placeholder is returned instead.
        /// </summary>
        public static SharedResource<Texture2DArray> GetTextureArray(string path, int spriteWidth, int spriteHeight, bool nonColor = false)
            => GetTextureArray($"{path}:{spriteWidth}:{spriteHeight}", new[] { path }, () => TextureFactory.LoadArray(path, spriteWidth, spriteHeight, nonColor));

        private static SharedResource<Texture2DArray> GetTextureArray(string key, string[] paths, Func<Texture2DArray> load)
        {
            if (textureArrayCache.TryGetValue(key, out var handle)) return handle.AddRef();

            foreach (string path in paths)
            {
                if (!ValidatePath(path)) return GetMissingTextureArray();
            }

            Texture2DArray textureArray;
            try
            {
                textureArray = load();
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not load texture array {key}: {e.Message}");
                return GetMissingTextureArray();
            }

            handle = SharedResource<Texture2DArray>.Create(textureArray);
            textureArrayCache.Add(key, handle);
            return handle.AddRef();
        }

        private static SharedResource<Texture2DArray> GetMissingTextureArray()
        {
            missingTextureArray ??= SharedResource<Texture2DArray>.Create(TextureFactory.CreateMissingTextureArray());
            return missingTextureArray.AddRef();
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

        /// <summary>
        /// Gets a loaded texture by its OpenGL handle. Throws if no loaded texture has that handle.
        /// </summary>
        public static SharedResource<Texture2D> GetTexture(uint handle)
        {
            if (textureHandleCache.TryGetValue(handle, out var resource))
                return resource.AddRef();

            throw new InvalidOperationException($"Texture with handle {handle} does not exist.");
        }
        #endregion

        #region Fonts
        /// <summary>
        /// Gets a font rendered for the current UI scale. Returns null if the file is missing.
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