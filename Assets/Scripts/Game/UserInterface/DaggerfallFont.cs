// Project:         Daggerfall Tools For Unity
// Copyright:       Copyright (C) 2009-2019 Daggerfall Workshop
// Web Site:        http://www.dfworkshop.net
// License:         MIT License (http://www.opensource.org/licenses/mit-license.php)
// Source Code:     https://github.com/Interkarma/daggerfall-unity
// Original Author: Gavin Clayton (interkarma@dfworkshop.net)
// Contributors:    
// 
// Notes:
//

using UnityEngine;
using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop.Utility;
using TMPro;

namespace DaggerfallWorkshop.Game.UserInterface
{
    /// <summary>
    /// Daggerfall-specific implementation of a pixel font.
    /// Supports classic FONT0000-0004 with an SDF variant.
    /// Classic font has the same limitations of 256 characters starting from ASCII 33.
    /// SDF font uses a keyed dictionary so can support any number of glyph codes.
    /// Current implementation will load a TextMeshPro 1.3.x font asset directly for SDF variant.
    /// </summary>
    public class DaggerfallFont
    {
        #region Fields

        public const int SpaceCode = 32;
        public const int ErrorCode = 63;
        const int defaultAsciiStart = 33;
        public const string invalidAsciiCode = "PixelFont does not contain glyph for ASCII code ";

        int glyphHeight;
        int glyphSpacing = 1;
        FilterMode filterMode = FilterMode.Point;
        Dictionary<int, GlyphInfo> glyphs = new Dictionary<int, GlyphInfo>();

        FontName font;
        FntFile fntFile = new FntFile();
        Color backgroundColor = Color.clear;
        Color textColor = Color.white;
        protected Texture2D atlasTexture;
        protected Rect[] atlasRects;
        protected int asciiStart = defaultAsciiStart;

        protected SDFFontInfo? sdfFontInfo;

        #endregion

        #region Structs & Enums

        public enum FontName
        {
            FONT0000,
            FONT0001,
            FONT0002,
            FONT0003,
            FONT0004,
        }

        public struct GlyphInfo
        {
            public Color32[] colors;
            public int width;
        }

        public struct SDFFontInfo
        {
            public float pointSize;
            public float baseline;
            public Texture2D atlas;
            public Dictionary<int, SDFGlyphInfo> glyphs;
        }

        public struct SDFGlyphInfo
        {
            public Rect rect;
            public Vector2 offset;
            public Vector2 size;
            public float advance;
        }

        #endregion

        #region Properties

        public int AsciiStart
        {
            get { return asciiStart; }
        }

        public int GlyphHeight
        {
            get { return glyphHeight; }
            set { glyphHeight = value; }
        }

        public int GlyphSpacing
        {
            get { return glyphSpacing; }
            set { glyphSpacing = value; }
        }

        public FilterMode FilterMode
        {
            get { return filterMode; }
            set { filterMode = value; }
        }

        public int GlyphCount
        {
            get { return glyphs.Count; }
        }

        public bool IsSDFCapable
        {
            get { return (DaggerfallUnity.Settings.SDFFontRendering && sdfFontInfo != null); }
        }

        #endregion

        #region Constructors

        public DaggerfallFont(FontName font = FontName.FONT0003)
        {
            this.font = font;
            LoadFont();
        }

        public DaggerfallFont(string arena2Path, FontName font = FontName.FONT0003)
        {
            this.font = font;
            LoadFont();
        }

        #endregion

        #region Glyph Rendering

        void DrawClassicGlyph(byte rawAscii, Rect targetRect, Color color)
        {
            Rect atlasRect = atlasRects[rawAscii - asciiStart];
            Graphics.DrawTexture(targetRect, atlasTexture, atlasRect, 0, 0, 0, 0, color, DaggerfallUI.Instance.PixelFontMaterial);
        }

        void DrawClassicGlyphWithShadow(byte rawAscii, Rect targetRect, Color color, Vector2 shadowPosition, Color shadowColor)
        {
            if (shadowPosition != Vector2.zero && shadowColor != Color.clear)
            {
                Rect shadowRect = targetRect;
                shadowRect.x += shadowPosition.x;
                shadowRect.y += shadowPosition.y;
                DrawClassicGlyph(rawAscii, shadowRect, shadowColor);
            }

            DrawClassicGlyph(rawAscii, targetRect, color);
        }

        void DrawSDFText(
            string text,
            Vector2 position,
            Vector2 scale,
            Color color)
        {
            float scalingRatio = GlyphHeight / sdfFontInfo.Value.pointSize * scale.y;
            byte[] utf32Bytes = Encoding.UTF32.GetBytes(text);
            for (int i = 0; i < utf32Bytes.Length; i += sizeof(int))
            {
                // Get code and use ? for any character code not in dictionary
                int code = BitConverter.ToInt32(utf32Bytes, i);
                if (!sdfFontInfo.Value.glyphs.ContainsKey(code))
                    code = ErrorCode;

                // Get glyph data for this code
                SDFGlyphInfo glyph = sdfFontInfo.Value.glyphs[code];

                // Handle space glyph by just advancing position
                if (code == SpaceCode)
                {
                    position.x += glyph.advance * scalingRatio;
                    continue;
                }

                // Compose target rect - this will change based on current display scale
                // Can use classic glyph height to approximate baseline vertical position
                float baseline = position.y - 2 * scale.y + GlyphHeight * scale.y + sdfFontInfo.Value.baseline;
                float xpos = position.x + glyph.offset.x * scalingRatio;
                float ypos = baseline - glyph.offset.y * scalingRatio;
                Rect targetRect = new Rect(xpos, ypos, glyph.size.x * scalingRatio, glyph.size.y * scalingRatio);

                // Draw glyph and advance position
                Graphics.DrawTexture(targetRect, sdfFontInfo.Value.atlas, glyph.rect, 0, 0, 0, 0, color, DaggerfallUI.Instance.SDFFontMaterial);
                position.x += glyph.advance * scalingRatio;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Draws a classic glyph with a drop-shadow.
        /// </summary>
        public void DrawClassicGlyph(byte rawAscii, Rect targetRect, Color color, Vector2 shadowPosition, Color shadowColor)
        {
            if (rawAscii < asciiStart)
                return;

            DrawClassicGlyphWithShadow(rawAscii, targetRect, color, shadowPosition, shadowColor);
        }

        /// <summary>
        /// Draws string of classic glyphs for simple text rendering.
        /// </summary>
        public void DrawText(
            string text,
            Vector2 position,
            Vector2 scale,
            Color color)
        {
            if (!fntFile.IsLoaded)
                throw new Exception("DaggerfallFont: DrawText() font not loaded.");

            // Redirect SDF rendering when enabled
            if (IsSDFCapable)
            {
                DrawSDFText(text, position, scale, color);
                return;
            }

            atlasTexture.filterMode = FilterMode;

            byte[] asciiBytes = Encoding.ASCII.GetBytes(text);
            if (asciiBytes == null || asciiBytes.Length == 0)
                return;

            float x = position.x;
            float y = position.y;
            for (int i = 0; i < asciiBytes.Length; i++)
            {
                // Invalid ASCII bytes are cast to a space character
                if (!HasGlyph(asciiBytes[i]))
                    asciiBytes[i] = SpaceCode;

                GlyphInfo glyph = GetGlyph(asciiBytes[i]);

                if (asciiBytes[i] != SpaceCode)
                {
                    Rect rect = new Rect(x, y, glyph.width * scale.x, GlyphHeight * scale.y);
                    DrawClassicGlyph(asciiBytes[i], rect, color);
                    x += rect.width + GlyphSpacing * scale.x;
                }
                else
                {
                    // Just add space character
                    Rect rect = new Rect(x, y, glyph.width * scale.x, GlyphHeight * scale.y);
                    x += rect.width;
                }
            }
        }

        /// <summary>
        /// Draws string of individual text glyphs with a shadow.
        /// </summary>
        public void DrawText(
            string text,
            Vector2 position,
            Vector2 scale,
            Color color,
            Color shadowColor,
            Vector2 shadowPos)
        {
            DrawText(text, position + shadowPos, scale, shadowColor);
            DrawText(text, position, scale, color);
        }

        /// <summary>
        /// Calculate width of text using whichever font path is active (classic or SDF).
        /// </summary>
        /// <param name="text">Text to calculate width of.</param>
        /// <param name="scale">Scale to use when calculating width.</param>
        /// <returns>Width of string in scaled pixels.</returns>
        public float CalculateTextWidth(string text, Vector2 scale, int start = 0, int length = -1)
        {
            // Must have a string
            if (string.IsNullOrEmpty(text))
                return 0;

            // Get automatic length from start position to end of text
            if (length < 0)
                length = text.Length - start;

            // Get substring if required
            if (start > 0 || length != text.Length)
                text = text.Substring(start, length);

            // Calculate width based on active font path
            float width = 0;
            if (!IsSDFCapable)
            {
                // Classic glyphs
                byte[] asciiBytes = Encoding.ASCII.GetBytes(text);
                for (int i = 0; i < asciiBytes.Length; i++)
                {
                    // Get code and use ? for any character code not in dictionary
                    int code = asciiBytes[i];
                    if (!HasGlyph(code))
                        code = ErrorCode;

                    // Get glyph data for this code and increment width
                    GlyphInfo glyph = GetGlyph(code);
                    width += glyph.width + GlyphSpacing;
                }
            }
            else
            {
                // SDF glyphs
                float scalingRatio = GlyphHeight / sdfFontInfo.Value.pointSize * scale.y;
                byte[] utf32Bytes = Encoding.UTF32.GetBytes(text);
                for (int i = 0; i < utf32Bytes.Length; i += sizeof(int))
                {
                    // Get code and use ? for any character code not in dictionary
                    int code = BitConverter.ToInt32(utf32Bytes, i);
                    if (!sdfFontInfo.Value.glyphs.ContainsKey(code))
                        code = ErrorCode;

                    // Get glyph data for this code and increment width
                    SDFGlyphInfo glyph = sdfFontInfo.Value.glyphs[code];
                    width += glyph.advance * scalingRatio;
                }
            }

            return width;
        }

        /// <summary>
        /// Reloads font glyphs with a different base colour (default is Color.white for normal UI tinting).
        /// This is an expensive operation, only use this at font create time.
        /// </summary>
        /// <param name="color">New colour of glyphs.</param>
        /// <returns>True if successful.</returns>
        public bool ReloadFont(Color color)
        {
            textColor = color;
            return LoadFont();
        }

        public void ClearGlyphs()
        {
            glyphs.Clear();
        }

        public bool HasGlyph(int ascii)
        {
            return glyphs.ContainsKey(ascii);
        }

        public void AddGlyph(int ascii, GlyphInfo info)
        {
            glyphs.Add(ascii, info);
        }

        public GlyphInfo GetGlyph(int ascii)
        {
            if (!glyphs.ContainsKey(ascii))
                throw new Exception(invalidAsciiCode + ascii);

            return glyphs[ascii];
        }

        public int GetGlyphWidth(int ascii)
        {
            if (!glyphs.ContainsKey(ascii))
                throw new Exception(invalidAsciiCode + ascii);

            return glyphs[ascii].width;
        }

        public void RemoveGlyph(int ascii)
        {
            if (!glyphs.ContainsKey(ascii))
                throw new Exception(invalidAsciiCode + ascii);

            glyphs.Remove(ascii);
        }

        public Material GetMaterial()
        {
            return (IsSDFCapable) ? DaggerfallUI.Instance.SDFFontMaterial : DaggerfallUI.Instance.PixelFontMaterial;
        }

        public void TryLoadSDFFont(string path)
        {
            // Attempt to load a TextMeshPro font asset
            TMP_FontAsset tmpFont = Resources.Load<TMP_FontAsset>(path);
            if (!tmpFont)
                return;

            // Create font info
            SDFFontInfo fi = new SDFFontInfo();
            fi.pointSize = tmpFont.fontInfo.PointSize;
            fi.atlas = tmpFont.atlas;
            fi.baseline = tmpFont.fontInfo.Baseline;
            fi.glyphs = new Dictionary<int, SDFGlyphInfo>();

            // Cache glyph info
            float atlasWidth = tmpFont.atlas.width;
            float atlasHeight = tmpFont.atlas.height;
            foreach (var kvp in tmpFont.characterDictionary)
            {
                // Compose glyph rect inside of atlas
                TMP_Glyph glyph = kvp.Value;
                float atlasGlyphX = glyph.x / atlasWidth;
                float atlasGlyphY = (atlasHeight - glyph.y - glyph.height) / atlasHeight;
                float atlasGlyphWidth = glyph.width / atlasWidth;
                float atlasGlyphHeight = glyph.height / atlasHeight;
                Rect atlasGlyphRect = new Rect(atlasGlyphX, atlasGlyphY, atlasGlyphWidth, atlasGlyphHeight);

                // Store information about this glyph
                SDFGlyphInfo glyphInfo = new SDFGlyphInfo()
                {
                    rect = atlasGlyphRect,
                    offset = new Vector2(glyph.xOffset, glyph.yOffset),
                    size = new Vector2(glyph.width, glyph.height),
                    advance = glyph.xAdvance,
                };
                fi.glyphs.Add(kvp.Key, glyphInfo);
            }

            // Set live font info
            sdfFontInfo = fi;
        }

        #endregion

        #region Private Methods

        bool LoadFont()
        {
            // Load font
            string filename = font.ToString() + ".FNT";
            if (!fntFile.Load(Path.Combine(DaggerfallUI.Instance.FontsFolder, filename), FileUsage.UseMemory, true))
                throw new Exception("DaggerfallFont failed to load font " + filename);

            // Start new glyph dictionary
            // Daggerfall fonts start at ASCII 33 '!' so we must create our own space glyph for ASCII 32
            ClearGlyphs();
            AddGlyph(SpaceCode, CreateSpaceGlyph());

            // Add remaining glyphs
            int ascii = asciiStart;
            for (int i = 0; i < FntFile.MaxGlyphCount; i++)
            {
                AddGlyph(ascii++, CreateGlyph(i));
            }

            GlyphHeight = fntFile.FixedHeight;

            // Create font atlas
            ImageProcessing.CreateFontAtlas(fntFile, Color.clear, Color.white, out atlasTexture, out atlasRects);
            atlasTexture.filterMode = FilterMode;

            // Load an SDF font variant if one is available
            TryLoadSDFFont(string.Format("Fonts/{0}-SDF", font.ToString()));

            return true;
        }

        GlyphInfo CreateSpaceGlyph()
        {
            int width = fntFile.FixedWidth - 1;
            int height = fntFile.FixedHeight;
            Color32[] colors = new Color32[width * height];
            for (int i = 0; i < colors.Length; i++)
            {
                colors[i] = backgroundColor;
            }

            GlyphInfo glyph = new GlyphInfo();
            glyph.colors = colors;
            glyph.width = width;

            return glyph;
        }

        GlyphInfo CreateGlyph(int index)
        {
            GlyphInfo glyph = new GlyphInfo();
            glyph.colors = ImageProcessing.GetProportionalGlyphColors(fntFile, index, backgroundColor, textColor, true);
            glyph.width = fntFile.GetGlyphWidth(index);

            return glyph;
        }

        #endregion
    }
}