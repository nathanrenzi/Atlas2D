using System.Drawing;
using System.Drawing.Imaging;
using static Atlas2D.OpenGL.GL;

namespace Atlas2D.Rendering
{
    public static class TextureFactory
    {
        internal static unsafe Texture2D CreateMissingTexture()
        {
            const int size = 8;
            byte[] pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    bool magenta = (x / 4 + y / 4) % 2 == 0;
                    int i = (x + y * size) * 4;
                    pixels[i] = magenta ? (byte)255 : (byte)0;
                    pixels[i + 1] = 0;
                    pixels[i + 2] = magenta ? (byte)255 : (byte)0;
                    pixels[i + 3] = 255;
                }
            }

            uint handle = glGenTexture();
            glActiveTexture(GL_TEXTURE0);
            glBindTexture(GL_TEXTURE_2D, handle);
            fixed (byte* data = pixels)
            {
                glTexImage2D(GL_TEXTURE_2D, 0, GL_SRGB_ALPHA, size, size, 0, GL_BGRA, GL_UNSIGNED_BYTE, (IntPtr)data);
            }
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);

            return new Texture2D(handle, size, size, "", false);
        }

        public static Texture2D Load(string filePath, bool nonColor)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(filePath);
            }

            uint handle = glGenTexture();
            glActiveTexture(GL_TEXTURE0);
            glBindTexture(GL_TEXTURE_2D, handle);

            using var image = new Bitmap(filePath);
            image.RotateFlip(RotateFlipType.RotateNoneFlipY);
            var data = image.LockBits(
                new Rectangle(0, 0, image.Width, image.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
   
            glTexImage2D(GL_TEXTURE_2D, 0, nonColor ? GL_RGBA : GL_SRGB_ALPHA, image.Width, image.Height, 0, GL_BGRA, GL_UNSIGNED_BYTE, data.Scan0);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);
            image.UnlockBits(data);

            return new Texture2D(handle, image.Width, image.Height, filePath, nonColor);
        }

        public static Texture2DArray LoadArray(string filePath, int layers, bool nonColor)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(filePath);
            }

            uint handle = glGenTexture();
            glActiveTexture(GL_TEXTURE0);
            glBindTexture(GL_TEXTURE_2D_ARRAY, handle);

            using var image = new Bitmap(filePath);
            image.RotateFlip(RotateFlipType.RotateNoneFlipY);
            var data = image.LockBits(
                new Rectangle(0, 0, image.Width, image.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, nonColor ? GL_RGBA : GL_SRGB_ALPHA, image.Width, image.Height/layers, layers, 0, GL_BGRA, GL_UNSIGNED_BYTE, data.Scan0);

            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, GL_REPEAT);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, GL_REPEAT);
            image.UnlockBits(data);

            return new Texture2DArray(handle, image.Width, image.Height/layers, layers, nonColor);
        }

        public static Texture2DArray LoadArray(string filePath, int spriteWidth, int spriteHeight, bool nonColor)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException(filePath);
            }

            uint handle = glGenTexture();
            glActiveTexture(GL_TEXTURE0);
            glBindTexture(GL_TEXTURE_2D_ARRAY, handle);

            // Spritesheet Image
            var spritesheetImage = new Bitmap(filePath);
            spritesheetImage.RotateFlip(RotateFlipType.RotateNoneFlipY);

            if (spriteWidth > spritesheetImage.Width || spriteHeight > spritesheetImage.Height)
            {
                Debug.LogError($"TEXTURE FACTORY: Incorrect sprite size. Cannot convert spritesheet {filePath} into texture array" +
                    $"\nInput Width: {spriteWidth} Input Height: {spriteHeight} Texture Full Width: {spritesheetImage.Width} Texture Full Height: {spritesheetImage.Height}");
                return null;
            }

            // Calculating # of loops
            int horizontalLoops = (int)MathF.Floor(spritesheetImage.Width / (float)spriteWidth);
            int verticalLoops = (int)MathF.Floor(spritesheetImage.Height / (float)spriteHeight);

            // Creating texture array in memory
            glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, nonColor ? GL_RGBA : GL_SRGB_ALPHA, spriteWidth, spriteHeight, horizontalLoops*verticalLoops, 0, GL_BGRA, GL_UNSIGNED_BYTE, IntPtr.Zero);

            // Subbing in data from the spritesheet
            for (int y = 0; y < verticalLoops; y++)
            {
                for (int x = 0; x < horizontalLoops; x++)
                {
                    // Cloning each sprite from spritesheet into a bitmap
                    Bitmap b = spritesheetImage.Clone(new Rectangle(x * spriteWidth, y * spriteHeight, spriteWidth, spriteHeight), spritesheetImage.PixelFormat);
                    var data = b.LockBits(
                        new Rectangle(0, 0, b.Width, b.Height),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format32bppArgb);

                    glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, x + (y * horizontalLoops), b.Width, b.Height, 1, GL_BGRA, GL_UNSIGNED_BYTE, data.Scan0);

                    b.UnlockBits(data);
                    b.Dispose();
                }
            }

            spritesheetImage.Dispose();

            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, GL_REPEAT);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, GL_REPEAT);

            return new Texture2DArray(handle, spriteWidth, spriteHeight, horizontalLoops * verticalLoops, nonColor);
        }

        public static Texture2DArray LoadArray(string[] filePaths, bool nonColor)
        {
            foreach (var path in filePaths)
            {
                if (!File.Exists(path))
                {
                    throw new FileNotFoundException(path);
                }
            }

            uint handle = glGenTexture();
            glActiveTexture(GL_TEXTURE0);
            glBindTexture(GL_TEXTURE_2D_ARRAY, handle);

            // First image
            using var firstImage = new Bitmap(filePaths[0]);
            firstImage.RotateFlip(RotateFlipType.RotateNoneFlipY);
            var firstData = firstImage.LockBits(
                new Rectangle(0, 0, firstImage.Width, firstImage.Height),
                ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);

            glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, nonColor ? GL_RGBA : GL_SRGB_ALPHA, firstImage.Width, firstImage.Height, filePaths.Length, 0, GL_BGRA, GL_UNSIGNED_BYTE, IntPtr.Zero);
            glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, 0, firstImage.Width, firstImage.Height, 1, GL_BGRA, GL_UNSIGNED_BYTE, firstData.Scan0);

            for (int i = 1; i < filePaths.Length; i++)
            {
                using var image = new Bitmap(filePaths[0]);
                image.RotateFlip(RotateFlipType.RotateNoneFlipY);
                var data = image.LockBits(
                    new Rectangle(0, 0, firstImage.Width, firstImage.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);

                if(image.Width != firstImage.Width || image.Height != firstImage.Height)
                {
                    Debug.LogError($"TEXTURE FACTORY: Cannot load image {filePaths[i]}, size is not the same as the first image in the array.");
                    continue;
                }

                glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, i, image.Width, image.Height, 1, GL_BGRA, GL_UNSIGNED_BYTE, data.Scan0);

                image.UnlockBits(data);
            }

            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, GL_REPEAT);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, GL_REPEAT);
            firstImage.UnlockBits(firstData);

            return new Texture2DArray(handle, firstImage.Width, firstImage.Height, filePaths.Length, nonColor);
        }

        public static unsafe Texture2D Create(int width, int height)
        {
            return Create(width, height, GL_RGBA, GL_RGBA, GL_NEAREST, GL_REPEAT);
        }

        public static unsafe Texture2D Create(int width, int height, int glColorInternalFormat,
            int glColorFormat, int glTextureFilterSetting, int glTextureWrapSetting)
        {
            uint handle = glGenTexture();
            glActiveTexture(GL_TEXTURE0);
            glBindTexture(GL_TEXTURE_2D, handle);
            glTexImage2D(GL_TEXTURE_2D, 0, glColorInternalFormat, width, height, 0, glColorFormat, GL_UNSIGNED_BYTE, NULL);

            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, glTextureFilterSetting);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, glTextureFilterSetting);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, glTextureWrapSetting);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, glTextureWrapSetting);

            return new Texture2D(handle, width, height, null, glColorInternalFormat != GL_SRGB_ALPHA);
        }
    }
}
