using System.Drawing;
using System.Drawing.Imaging;
using static Atlas2D.OpenGL.GL;

namespace Atlas2D.Rendering
{
    public static class TextureFactory
    {
        private const int MISSING_TEXTURE_SIZE = 8;

        internal static unsafe Texture2D CreateMissingTexture()
        {
            byte[] pixels = CreateMissingTexturePixels();

            uint handle = glGenTexture();
            glActiveTexture(GL_TEXTURE0);
            glBindTexture(GL_TEXTURE_2D, handle);
            fixed (byte* data = pixels)
            {
                glTexImage2D(GL_TEXTURE_2D, 0, GL_SRGB_ALPHA, MISSING_TEXTURE_SIZE, MISSING_TEXTURE_SIZE, 0, GL_BGRA, GL_UNSIGNED_BYTE, (IntPtr)data);
            }
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_S, GL_REPEAT);
            glTexParameteri(GL_TEXTURE_2D, GL_TEXTURE_WRAP_T, GL_REPEAT);

            return new Texture2D(handle, MISSING_TEXTURE_SIZE, MISSING_TEXTURE_SIZE, "", false);
        }

        /// <summary>
        /// Creates a one layer texture array with the same magenta and black checkerboard as <see cref="CreateMissingTexture"/>.
        /// </summary>
        internal static unsafe Texture2DArray CreateMissingTextureArray()
        {
            byte[] pixels = CreateMissingTexturePixels();

            uint handle = glGenTexture();
            glActiveTexture(GL_TEXTURE0);
            glBindTexture(GL_TEXTURE_2D_ARRAY, handle);
            fixed (byte* data = pixels)
            {
                glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, GL_SRGB_ALPHA, MISSING_TEXTURE_SIZE, MISSING_TEXTURE_SIZE, 1, 0, GL_BGRA, GL_UNSIGNED_BYTE, (IntPtr)data);
            }
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, GL_REPEAT);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, GL_REPEAT);

            return new Texture2DArray(handle, MISSING_TEXTURE_SIZE, MISSING_TEXTURE_SIZE, 1, false);
        }

        private static byte[] CreateMissingTexturePixels()
        {
            byte[] pixels = new byte[MISSING_TEXTURE_SIZE * MISSING_TEXTURE_SIZE * 4];
            for (int y = 0; y < MISSING_TEXTURE_SIZE; y++)
            {
                for (int x = 0; x < MISSING_TEXTURE_SIZE; x++)
                {
                    bool magenta = (x / 4 + y / 4) % 2 == 0;
                    int i = (x + y * MISSING_TEXTURE_SIZE) * 4;
                    pixels[i] = magenta ? (byte)255 : (byte)0;
                    pixels[i + 1] = 0;
                    pixels[i + 2] = magenta ? (byte)255 : (byte)0;
                    pixels[i + 3] = 255;
                }
            }
            return pixels;
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

        /// <summary>
        /// Loads an image made of equally sized layers stacked vertically. Layer 0 is the top slice.
        /// </summary>
        public static Texture2DArray LoadArray(string filePath, int layers, bool nonColor)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);

            using var image = new Bitmap(filePath);
            if (layers <= 0 || image.Height % layers != 0)
                throw new ArgumentException($"{filePath} is {image.Height} pixels tall, which can't be split into {layers} equal layers.");

            return LoadSheet(image, image.Width, image.Height / layers, nonColor, filePath);
        }

        /// <summary>
        /// Loads a spritesheet with one layer per sprite. Layers go left to right, then top to bottom.
        /// Leftover pixels on the right or bottom edge that don't fill a whole sprite are ignored.
        /// </summary>
        public static Texture2DArray LoadArray(string filePath, int spriteWidth, int spriteHeight, bool nonColor)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);

            using var image = new Bitmap(filePath);
            if (spriteWidth <= 0 || spriteHeight <= 0 || spriteWidth > image.Width || spriteHeight > image.Height)
                throw new ArgumentException($"Can't split {filePath} ({image.Width}x{image.Height}) into {spriteWidth}x{spriteHeight} sprites.");

            return LoadSheet(image, spriteWidth, spriteHeight, nonColor, filePath);
        }

        /// <summary>
        /// Loads one layer per image, in the order given. Every image must be the same size.
        /// </summary>
        public static Texture2DArray LoadArray(string[] filePaths, bool nonColor)
        {
            if (filePaths.Length == 0) throw new ArgumentException("Can't create a texture array from no images.");
            foreach (string path in filePaths)
            {
                if (!File.Exists(path)) throw new FileNotFoundException(path);
            }

            List<Bitmap> images = new List<Bitmap>();
            try
            {
                foreach (string path in filePaths)
                {
                    Bitmap image = new Bitmap(path);
                    images.Add(image);
                    if (image.Width != images[0].Width || image.Height != images[0].Height)
                        throw new ArgumentException($"{path} is {image.Width}x{image.Height}, but every layer must match the first image ({images[0].Width}x{images[0].Height}).");
                }

                uint handle = CreateArrayStorage(images[0].Width, images[0].Height, images.Count, nonColor, filePaths[0]);
                for (int i = 0; i < images.Count; i++)
                {
                    UploadLayer(images[i], i);
                }
                return new Texture2DArray(handle, images[0].Width, images[0].Height, images.Count, nonColor);
            }
            finally
            {
                foreach (Bitmap image in images) image.Dispose();
            }
        }

        private static Texture2DArray LoadSheet(Bitmap sheet, int spriteWidth, int spriteHeight, bool nonColor, string filePath)
        {
            int columns = sheet.Width / spriteWidth;
            int rows = sheet.Height / spriteHeight;
            uint handle = CreateArrayStorage(spriteWidth, spriteHeight, columns * rows, nonColor, filePath);

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    Rectangle area = new Rectangle(column * spriteWidth, row * spriteHeight, spriteWidth, spriteHeight);
                    using Bitmap sprite = sheet.Clone(area, PixelFormat.Format32bppArgb);
                    UploadLayer(sprite, column + row * columns);
                }
            }

            return new Texture2DArray(handle, spriteWidth, spriteHeight, columns * rows, nonColor);
        }

        private static uint CreateArrayStorage(int width, int height, int layers, bool nonColor, string filePath)
        {
            int maxLayers = glGetInteger(GL_MAX_ARRAY_TEXTURE_LAYERS);
            if (layers > maxLayers)
                throw new ArgumentException($"{filePath} needs {layers} layers, but this graphics card supports at most {maxLayers} per texture array.");

            uint handle = glGenTexture();
            glActiveTexture(GL_TEXTURE0);
            glBindTexture(GL_TEXTURE_2D_ARRAY, handle);
            glTexImage3D(GL_TEXTURE_2D_ARRAY, 0, nonColor ? GL_RGBA : GL_SRGB_ALPHA, width, height, layers, 0, GL_BGRA, GL_UNSIGNED_BYTE, IntPtr.Zero);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MIN_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_MAG_FILTER, GL_NEAREST);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_S, GL_CLAMP_TO_EDGE);
            glTexParameteri(GL_TEXTURE_2D_ARRAY, GL_TEXTURE_WRAP_T, GL_CLAMP_TO_EDGE);
            return handle;
        }

        // Each layer is flipped so UV (0, 0) is its bottom left, matching regular textures
        private static void UploadLayer(Bitmap image, int layer)
        {
            image.RotateFlip(RotateFlipType.RotateNoneFlipY);
            var data = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                glTexSubImage3D(GL_TEXTURE_2D_ARRAY, 0, 0, 0, layer, image.Width, image.Height, 1, GL_BGRA, GL_UNSIGNED_BYTE, data.Scan0);
            }
            finally
            {
                image.UnlockBits(data);
            }
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
