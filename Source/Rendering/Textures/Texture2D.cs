
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using static Atlas2D.OpenGL.GL;

namespace Atlas2D.Rendering
{
    public class Texture2D : ITexture, IDisposable
    {
        private bool _disposed;
        public uint Handle { get; private set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string FilePath { get; }
        public bool NonColor { get; }

        public Texture2D(uint handle, int width, int height, string filePath, bool nonColor)
        {
            Handle = handle;
            Width = width;
            Height = height;
            FilePath = filePath;
            NonColor = nonColor;
        }

        public uint GetHandle() => Handle;

        public Vector2 GetSize() => new Vector2(Width, Height);

        public void Use(int textureSlot)
        {
            glActiveTexture(textureSlot);
            glBindTexture(GL_TEXTURE_2D, Handle);
        }

        public unsafe void SetData(Rectangle bounds, byte[] data)
        {
            // Using not in-use texture slot since this does not cache itself as the last used texture anywhere
            Use(GL_TEXTURE0);
            fixed (byte* ptr = data)
            {
                glTexSubImage2D(GL_TEXTURE_2D, 0, bounds.Left, bounds.Top, bounds.Width, bounds.Height, GL_RGBA, GL_UNSIGNED_BYTE, ptr);
            }
        }

        public Bitmap GetData(int format = GL_BGRA)
        {
            Use(GL_TEXTURE0);
            Bitmap bitmap = new Bitmap(Width, Height);
            var data = bitmap.LockBits(
                        new Rectangle(0, 0, Width, Height),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format32bppArgb);

            glGetTexImage(GL_TEXTURE_2D, 0, format, GL_UNSIGNED_BYTE, data.Scan0);

            bitmap.UnlockBits(data);
            return bitmap;
        }

        public unsafe void Save(string filePath)
        {
            Use(GL_TEXTURE0);
            Bitmap bitmap = new Bitmap(Width, Height);
            var data = bitmap.LockBits(
                        new Rectangle(0, 0, Width, Height),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format32bppArgb);

            glGetTexImage(GL_TEXTURE_2D, 0, GL_BGRA, GL_UNSIGNED_BYTE, data.Scan0);

            bitmap.UnlockBits(data);
            bitmap.Save(filePath, ImageFormat.Png);
            bitmap.Dispose();
        }

        public void SetFilteringMode(bool linear)
        {
            Use(GL_TEXTURE0);

            if (linear)
            {
                // Linear filtering
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_LINEAR);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_LINEAR);
            }
            else
            {
                // Nearest filtering
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
                glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            }
        }

        private void Dispose(bool disposing)
        {
            if(!_disposed)
            {
                try
                {
                    glDeleteTexture(Handle);
                    _disposed = true;
                } catch(Exception e)
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
