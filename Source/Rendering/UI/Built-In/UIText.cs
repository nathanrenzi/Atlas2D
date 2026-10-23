using Atlas2D.Rendering;
using Atlas2D.Rendering.Shaders;
using Atlas2D.Rendering.Text;
using FreeTypeSharp.Native;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using static FreeTypeSharp.Native.FT;

namespace Atlas2D.UI
{
    /// <summary>
    /// A UI element that renders text with support for word wrapping, alignment, and overflow modes.
    /// </summary>
    public sealed class UIText : UIElement
    {
        private struct TextLine
        {
            public string Text;
            public Vector2 Size;
        }

        /// <summary>
        /// The text content to display.
        /// </summary>
        public string Text
        {
            get => _text;
            set
            {
                if (_text != value)
                {
                    _text = value;
                    InvalidateMeasure();
                }
            }
        }
        private string _text;

        /// <summary>
        /// The horizontal alignment of the text within the element's bounds.
        /// </summary>
        public TextAlignment HorizontalAlignment
        {
            get => _horizontalAlignment;
            set
            {
                if(_horizontalAlignment != value)
                {
                    _horizontalAlignment = value;
                    UpdateMesh();
                }
            }
        }
        private TextAlignment _horizontalAlignment;


        /// <summary>
        /// The vertical alignment of the text within the element's bounds.
        /// </summary>
        public TextAlignment VerticalAlignment
        {
            get => _verticalAlignment;
            set
            {
                if (_verticalAlignment != value)
                {
                    _verticalAlignment = value;
                    UpdateMesh();
                }
            }
        }
        private TextAlignment _verticalAlignment;

        /// <summary>
        /// How text is handled when it overflows the available area.
        /// </summary>
        public TextOverflowMode OverflowMode
        {
            get => _overflowMode;
            set
            {
                if(_overflowMode != value)
                {
                    _overflowMode = value;
                    InvalidateMeasure();
                }
            }
        }
        private TextOverflowMode _overflowMode;

        /// <summary>
        /// The line height multiplier applied to the font's default line height.
        /// </summary>
        public float LineHeightMultiplier
        {
            get => _lineHeightMultiplier;
            set
            {
                if(_lineHeightMultiplier != value)
                {
                    _lineHeightMultiplier = value;
                    InvalidateMeasure();
                }
            }
        }
        private float _lineHeightMultiplier;

        /// <summary>
        /// The glyph store used to render this text element's font.
        /// </summary>
        public SharedResource<FontGlyphStore> FontGlyphStore { get; private set; }

        private readonly FontArgs _fontArguments;

        private float _totalSizeX = 0;
        private float _totalSizeY = 0;
        private List<TextLine> _measuredTextLines = new List<TextLine>();
        private List<(Vector2, int)> _characterStartPositions = new();

        /// <summary>
        /// Creates a new <see cref="UIText"/>.
        /// </summary>
        /// <param name="style">The visual style of the text.</param>
        /// <param name="text">The initial text content to display.</param>
        public UIText(UITextStyle style, string text, UIRenderArgs? arguments = null)
            : base(arguments, true)
        {
            _fontArguments = style.FontArguments;
            FontGlyphStore = LoadFont();

            _text = text;
            HorizontalAlignment = style.HorizontalAlignment;
            VerticalAlignment = style.VerticalAlignment;
            OverflowMode = style.OverflowMode;
            LineHeightMultiplier = style.LineHeightMultiplier;

            SharedResource<Material> mat = SharedResource<Material>.Create(Material.Create(
                style.CustomShader ?? GlobalShaders.Text, style.Color, FontGlyphStore.Value.Texture));
            Renderer.SetMaterial(mat);
            mat.Release();

            UICanvas.Instance.OnUIScaleChanged += OnUIScaleChanged;
        }

        private SharedResource<FontGlyphStore> LoadFont() => Resources.GetFont(_fontArguments.FontFile,
            _fontArguments.FontSize, _fontArguments.FontScale, 0);

        private void OnUIScaleChanged(float scale)
        {
            SharedResource<FontGlyphStore> font = LoadFont();
            if (font == null || font.Value == FontGlyphStore.Value)
            {
                font?.Release();
                return;
            }

            Renderer.Material.Value.SetMainTexture(font.Value.Texture);
            FontGlyphStore.Release();
            FontGlyphStore = font;
            InvalidateMeasure();
        }

        protected override Vector2 MeasureCore(Vector2 availableSize)
        {
            _totalSizeX = 0;
            _totalSizeY = 0;

            availableSize = new Vector2(
                Math.Max(0, availableSize.X - Padding.Left - Padding.Right),
                Math.Max(0, availableSize.Y - Padding.Top - Padding.Bottom)
            );

            Vector2 size = Vector2.Zero;

            _measuredTextLines.Clear();
            if (string.IsNullOrEmpty(Text)) return new Vector2(
                MinSize.X + Padding.Left + Padding.Right,
                MinSize.Y + Padding.Top + Padding.Bottom
            );

            string normalized = Regex.Replace(Text, @"\s", " ");
            string[] parts = null;
            switch (OverflowMode)
            {
                case TextOverflowMode.Word:
                    parts = Regex.Split(normalized, @"( +)");
                    break;
                case TextOverflowMode.Character:
                    parts = normalized.ToCharArray().Select(x => x.ToString()).ToArray();
                    break;
                default:
                    parts = [normalized];
                    break;
            }

            StringBuilder lineBuilder = new();
            float currentSizeX = 0;
            float maxSizeY = 0;

            for (int i = 0; i < parts.Length; i++)
            {
                string token = parts[i];
                Vector2 tokenSize = MeasureString(token);

                bool isFirstTokenInLine = currentSizeX == 0;

                if (!isFirstTokenInLine && currentSizeX + tokenSize.X > availableSize.X)
                {
                    _measuredTextLines.Add(new TextLine()
                    {
                        Text = lineBuilder.ToString(),
                        Size = new Vector2(currentSizeX, maxSizeY)
                    });

                    lineBuilder.Clear();
                    _totalSizeX = MathF.Max(currentSizeX, _totalSizeX);
                    currentSizeX = 0;
                    maxSizeY = 0;
                }

                lineBuilder.Append(token);
                currentSizeX += tokenSize.X;
                maxSizeY = MathF.Max(maxSizeY, tokenSize.Y);
            }

            if (lineBuilder.Length > 0)
            {
                _measuredTextLines.Add(new TextLine()
                {
                    Text = lineBuilder.ToString(),
                    Size = new Vector2(currentSizeX, maxSizeY)
                });
            }

            _totalSizeX = MathF.Max(currentSizeX, _totalSizeX);
            _totalSizeY = FontGlyphStore.Value.Ascent
                + (_measuredTextLines.Count - 1) * (FontGlyphStore.Value.Arguments.FontSize * LineHeightMultiplier);

            return new Vector2(
                _totalSizeX + Padding.Left + Padding.Right,
                _totalSizeY + Padding.Top + Padding.Bottom
            );
        }

        private Vector2 MeasureString(string text)
        {
            Vector2 size = Vector2.Zero;
            uint previousIndex = 0;

            for (int i = 0; i < text.Length; i++)
            {
                char glyph = FontGlyphStore.Value.Resolve(text[i]);
                Character character = FontGlyphStore.Value.Characters[glyph];
                uint charIndex = FT_Get_Char_Index(FontGlyphStore.Value.Face, glyph);

                if(i > 0 && FontGlyphStore.Value.UseKerning &&
                    FT_Get_Kerning(FontGlyphStore.Value.Face, previousIndex, charIndex, (uint)FT_Kerning_Mode.FT_KERNING_DEFAULT, out FT_Vector delta) == FT_Error.FT_Err_Ok)
                {
                    size.X += delta.x >> 6;
                }

                size.X += character.Advance;
                size.Y = MathF.Max(character.Bearing.Y, size.Y);

                previousIndex = charIndex;
            }

            return size;
        }

        public override void UpdateMesh()
        {
            _characterStartPositions.Clear();
            List<float[]> tempVertexArrays = new List<float[]>();
            List<uint> tempIndices = new List<uint>();

            if (_measuredTextLines.Count == 0)
            {
                tempVertexArrays.Add([-10000, -10000, 0, 0]);
                tempVertexArrays.Add([-10000, -10000, 0, 0]);
                tempVertexArrays.Add([-10000, -10000, 0, 0]);
                tempIndices.Add(0);
                tempIndices.Add(1);
                tempIndices.Add(2);
            }
            else
            {
                Rect rect = GetLocalBounds();
                for (int i = 0; i < _measuredTextLines.Count; i++)
                {
                    TextLine line = _measuredTextLines[i];
                    float xPos = rect.X;
                    float yPos = rect.Y;
                    float lineOffset = i * FontGlyphStore.Value.Arguments.FontSize * LineHeightMultiplier;

                    switch (VerticalAlignment)
                    {
                        case TextAlignment.Top:
                            yPos += FontGlyphStore.Value.Ascent + lineOffset;
                            break;
                        case TextAlignment.Center:
                            yPos += FontGlyphStore.Value.Ascent + (rect.Height - _totalSizeY) / 2f + lineOffset;
                            break;
                        case TextAlignment.Bottom:
                            yPos += rect.Height - _totalSizeY + FontGlyphStore.Value.Ascent + lineOffset;
                            break;
                    }

                    switch(HorizontalAlignment)
                    {
                        case TextAlignment.Center:
                            xPos += (rect.Width - line.Size.X) / 2f;
                            break;
                        case TextAlignment.Right:
                            xPos += rect.Width - line.Size.X;
                            break;
                    }

                    if (i == 0) _characterStartPositions.Add((new Vector2(xPos, yPos), i));

                    uint previousIndex = 0;
                    uint charIndex = 0;

                    for (int k = 0; k < line.Text.Length; k++)
                    {
                        char c = line.Text[k];
                        char glyph = FontGlyphStore.Value.Resolve(c);
                        Character character = FontGlyphStore.Value.Characters[glyph];
                        charIndex = FT_Get_Char_Index(FontGlyphStore.Value.Face, glyph);

                        if(FontGlyphStore.Value.UseKerning)
                        {
                            if(FT_Get_Kerning(FontGlyphStore.Value.Face, previousIndex, charIndex,
                                (uint)FT_Kerning_Mode.FT_KERNING_DEFAULT, out FT_Vector delta) == FT_Error.FT_Err_Ok)
                            {
                                xPos += delta.x >> 6;
                            }
                        }

                        float xVertex = xPos + character.Bearing.X;
                        float yVertex = yPos - character.Bearing.Y;

                        if(!UseWorldPosition)
                        {
                            Vector2 snapped = UICanvas.Instance.VirtualToScreen(new Vector2(xVertex, yVertex));
                            snapped.X = MathF.Floor(snapped.X);
                            snapped.Y = MathF.Floor(snapped.Y);
                            snapped = UICanvas.Instance.ScreenToVirtual(snapped);
                            xVertex = snapped.X;
                            yVertex = snapped.Y;
                        }

                        float w = character.Size.X;
                        float h = character.Size.Y;

                        float L = character.UVX.X;
                        float R = character.UVX.Y;
                        float T = character.UVY.X;
                        float B = character.UVY.Y;

                        uint count = (uint)tempVertexArrays.Count;
                        tempVertexArrays.Add([xVertex, yVertex, L, T]);
                        tempVertexArrays.Add([xVertex + w, yVertex, R, T]);
                        tempVertexArrays.Add([xVertex + w, yVertex + h, R, B]);
                        tempVertexArrays.Add([xVertex, yVertex + h, L, B]);
                        tempIndices.Add(count + 0);
                        tempIndices.Add(count + 1);
                        tempIndices.Add(count + 2);
                        tempIndices.Add(count + 0);
                        tempIndices.Add(count + 2);
                        tempIndices.Add(count + 3);

                        _characterStartPositions.Add((new Vector2(character.Size.X <= 0 ? xPos + character.Advance
                            : xVertex + w, yPos), i));

                        xPos += character.Advance;

                        previousIndex = charIndex;
                    }
                }
            }

            List<float> tempVertices = new List<float>();
            for (int i = 0; i < tempVertexArrays.Count; i++)
            {
                for (int z = 0; z < tempVertexArrays[i].Length; z++)
                {
                    tempVertices.Add(tempVertexArrays[i][z]);
                }
            }

            Renderer.SetVertexArrays(tempVertices.ToArray(), tempIndices.ToArray(), !Renderer.IsLoaded, Renderer.IsLoaded);
        }

        /// <summary>
        /// Returns the position of the character at the given index in virtual space.
        /// </summary>
        /// <param name="index">The index of the character.</param>
        /// <returns>The position of the character in virtual space, or <see cref="Vector2.Zero"/> if the index is out of range.</returns>
        public Vector2 GetCharacterPositionAt(int index)
        {
            if(index < 0 || index >= _characterStartPositions.Count)
            {
                return Vector2.Zero;
            }

            return _characterStartPositions[index].Item1;
        }

        /// <summary>
        /// Returns the index of the character closest to the given position in virtual space.
        /// </summary>
        /// <param name="virtualPos">The position in virtual space.</param>
        /// <returns>The index of the nearest character.</returns>
        public int GetCharacterIndexAt(Vector2 virtualPos)
        {
            if (_characterStartPositions.Count == 0) return 0;

            Vector2 localPos = virtualPos - GetVirtualPosition();

            float lineHeight = FontGlyphStore.Value.Arguments.FontSize * LineHeightMultiplier;

            for (int i = 0; i < _characterStartPositions.Count - 1; i++)
            {
                (Vector2 startPos, int lineIndex) = _characterStartPositions[i];
                (Vector2 endPos, int endLineIndex) = _characterStartPositions[i + 1];

                float lineTop = startPos.Y - FontGlyphStore.Value.Arguments.FontSize;
                float lineBottom = startPos.Y;

                bool onThisLine = (lineIndex == 0 || localPos.Y >= lineTop) && localPos.Y <= lineBottom;
                if (!onThisLine) continue;

                if (lineIndex != endLineIndex)
                {
                    float charMidX = startPos.X + (endPos.X - startPos.X) / 2f;
                    return localPos.X >= charMidX ? i + 1 : i;
                }
                else
                {
                    if (localPos.X >= startPos.X && localPos.X < endPos.X)
                    {
                        float charMidX = startPos.X + (endPos.X - startPos.X) / 2f;
                        return localPos.X >= charMidX ? i + 1 : i;
                    }
                }
            }

            int endIndex = Math.Max(_characterStartPositions.Count - 1, 0);
            if (_characterStartPositions.Count > 0 && localPos.X > _characterStartPositions[endIndex].Item1.X)
            {
                return endIndex;
            }
            else if(localPos.X < 0 || localPos.Y < 0)
            {
                return 0;
            }
            else
            {
                return endIndex;
            }
        }

        protected override void OnDispose()
        {
            if (UICanvas.Instance != null) UICanvas.Instance.OnUIScaleChanged -= OnUIScaleChanged;
            FontGlyphStore.Release();
        }
    }
}
