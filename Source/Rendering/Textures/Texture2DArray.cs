using System.Numerics;
using static Atlas2D.OpenGL.GL;

namespace Atlas2D.Rendering
{
    public class Texture2DArray : IDisposable
    {
        private bool _disposed;
        private bool? _linearFiltering;
        public uint Handle { get; }
        public int Width { get; }
        public int Height { get; }
        public int Layers { get; }
        public bool NonColor { get; }

        public Texture2DArray(uint handle, int width, int height, int layers, bool nonColor)
        {
            Handle = handle;
            Width = width;
            Height = height;
            Layers = layers;
            NonColor = nonColor;
        }

        public Vector2 GetSize() => new Vector2(Width, Height);

        public void Use(int _textureSlot)
        {
            glActiveTexture(_textureSlot);
            glBindTexture(GL_TEXTURE_2D_ARRAY, Handle);
        }

        public void SetFilteringMode(bool linear)
        {
            if (_linearFiltering == linear) return;
            _linearFiltering = linear;
            Use(GL_TEXTURE0);

            if (linear)
            {
                // Linear filtering
                glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
                glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            }
            else
            {
                // Nearest filtering
                glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
                glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            }
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                try
                {
                    glDeleteTexture(Handle);
                    _disposed = true;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Could not dispose texture {Handle}: {e.Message}");
                }
            }
        }
        public void Dispose()
        {
            Dispose(true);
        }
    }
}
