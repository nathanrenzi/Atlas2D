using System.Numerics;
using static Atlas2D.OpenGL.GL;

namespace Atlas2D.Rendering
{
    public class Texture2DArray : ITexture, IDisposable
    {
        private bool _disposed;
        public uint Handle { get; private set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int Layers { get; set; }
        public bool NonColor { get; }
        public int GetTextureLayers() => Layers;

        public Texture2DArray(uint handle, int width, int height, int layers, bool nonColor)
        {
            Handle = handle;
            Width = width;
            Height = height;
            Layers = layers;
        }

        public uint GetHandle() => Handle;

        public Vector2 GetSize() => new Vector2(Width, Height);

        public void Use(int _textureSlot)
        {
            glActiveTexture(_textureSlot);
            glBindTexture(GL_TEXTURE_2D_ARRAY, Handle);
        }

        public void SetFilteringMode(bool _linear)
        {
            Use(GL_TEXTURE0);

            if (_linear)
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

        public void Dispose(bool _disposing)
        {
            if (!_disposed)
            {
                glDeleteTexture(Handle);
                _disposed = true;
            }
        }
        public void Dispose()
        {
            Dispose(true);
        }
    }
}
