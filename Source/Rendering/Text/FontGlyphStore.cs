using static FreeTypeSharp.Native.FT;
using static Atlas2D.OpenGL.GL;
using FreeTypeSharp;

namespace Atlas2D.Rendering.Text
{
    public class FontGlyphStore : IDisposable
    {
        public SharedResource<Texture2D> Texture { get; }
        public Dictionary<char, Character> Characters { get; } = new Dictionary<char, Character>();
        public FontArgs Arguments { get; }
        public FreeTypeLibrary Library { get; }
        public IntPtr Face { get; }
        public bool UseKerning { get; }
        public int Ascent { get; private set; }
        public int Descent { get; private set; }
        public char FallbackCharacter { get; private set; } = '?';
        private bool _isDone = false;

        public FontGlyphStore(SharedResource<Texture2D> texture, int fontSize, string fontFile, FreeTypeLibrary library, IntPtr face, bool useKerning)
        {
            Texture = texture.AddRef();
            Library = library;
            Face = face;
            UseKerning = useKerning;

            Arguments = new FontArgs() { FontSize = fontSize, FontFile = fontFile };
        }

        public void AddCharacter(char code, Character character)
        {
            if(_isDone) return;
            Characters.Add(code, character);
        }

        public void Done()
        {
            if (_isDone) return;
            _isDone = true;

            if (Characters.ContainsKey('?')) FallbackCharacter = '?';
            else if (Characters.ContainsKey(' ')) FallbackCharacter = ' ';
            else if (Characters.Count > 0) FallbackCharacter = Characters.Keys.First();
            else Debug.LogError($"FONT: {Arguments.FontFile} has no glyphs.");

            foreach (var character in Characters.Values)
            {
                int descent = (int)(character.Size.Y - character.Bearing.Y);
                int ascent = (int)(character.Size.Y - descent);
                if(ascent > Ascent)
                {
                    Ascent = ascent;
                }
                if(descent > Descent)
                {
                    Descent = descent;
                }
            }
        }

        /// <summary>
        /// Returns true if the font has a glyph for this character.
        /// </summary>
        public bool HasCharacter(char c) => Characters.ContainsKey(c);

        /// <summary>
        /// Returns the character to draw for c: c itself if the font has it, otherwise <see cref="FallbackCharacter"/>.
        /// </summary>
        public char Resolve(char c) => Characters.ContainsKey(c) ? c : FallbackCharacter;

        public void Dispose()
        {
            Characters.Clear();
            FT_Done_Face(Face);
            Texture.Release();
            Library.Dispose();
        }
    }
}