using Atlas2D.Rendering;
using System.Numerics;

namespace Atlas2D
{
    /// <summary>
    /// This class allows for spritesheet calculations and stores the data of all active spritesheets
    /// </summary>
    public static class Spritesheets
    {
        public static IDictionary<Texture2D, SpritesheetElement> spritesheets = new Dictionary<Texture2D, SpritesheetElement>();

        public static Vector2 GetVertexUVNonElement(int _totalSpriteColumns,
            int _totalSpriteRows, int _sampleColumn, int _sampleRow, Vector2 _localUV)
        {
            Vector2 stride = new Vector2(1f / _totalSpriteColumns, 1f / _totalSpriteRows);

            return new Vector2((stride.X * _sampleColumn) + (_localUV.X * stride.X),
                (stride.Y * _sampleRow) + (_localUV.Y * stride.Y));
        }

        public static void Add(Texture2D texture, int totalSpriteColumns, int totalSpriteRows)
        {
            if (spritesheets.ContainsKey(texture))
            {
                Debug.LogWarning($"Trying to add a spritesheet that already exists.");
                return;
            }
            spritesheets.Add(texture, new SpritesheetElement(totalSpriteColumns, totalSpriteRows));
        }

        public static Vector2 GetVertexUV(Texture2D texture, int col, int row, Vector2 localUV)
        {
            if(!spritesheets.ContainsKey(texture))
            {
                Debug.LogWarning("Requested a spritesheet that does not exist.");
                return localUV;
            }

            if (col >= spritesheets[texture].totalSpriteColumns || row >= spritesheets[texture].totalSpriteRows)
            {
                Debug.LogWarning("Requested a sprite that is out of the bounds of it's texture.");
                return localUV;
            }

            // The UV stride of each sprite in the spritesheet
            Vector2 stride = new Vector2(1f / spritesheets[texture].totalSpriteColumns, 1f / spritesheets[texture].totalSpriteRows);

            return new Vector2((stride.X * col) + (localUV.X * stride.X), (stride.Y * row) + (localUV.Y * stride.Y));
        }
    }
}
