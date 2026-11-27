using Overcore.Components;
using Overcore.Core;
using Overcore.Rendering;
using Overcore.Rendering.UI;
using OpenTK.Graphics.OpenGL4;
using System.Numerics;

namespace Overcore.Rendering.OpenTK;

/// <summary>Alpha-blended screen-space overlay renderer for Canvas graphics and text.</summary>
internal sealed class OpenGLUIRenderer : IDisposable
{
    private const int FloatsPerVertex = 9;

    private readonly ShaderProgram _shader;
    private readonly int _vao;
    private readonly int _vbo;
    private readonly int _sourceTextureLocation;
    private readonly ImGuiController? _fontSource;
    private readonly IResourceManager? _resources;
    private readonly Dictionary<string, int> _textureHandles = new(StringComparer.OrdinalIgnoreCase);
    private readonly UIEventSystem _events = new();
    private static readonly Text DefaultInputFieldText = new() { FontSize = 24f };
    private static readonly Vector3 FocusOutlineColor = new(0.45f, 0.76f, 1f);

    private sealed class UIBatch(int textureHandle, RectF? clipRect)
    {
        public int TextureHandle { get; } = textureHandle;
        public RectF? ClipRect { get; } = clipRect;
        public List<float> Vertices { get; } = [];
    }
    private bool _disposed;

    public OpenGLUIRenderer(ImGuiController? fontSource, IResourceManager? resources)
    {
        _fontSource = fontSource;
        _resources = resources;
        const string vertex = """
            #version 330 core
            layout (location = 0) in vec2 aPosition;
            layout (location = 1) in vec2 aUv;
            layout (location = 2) in vec4 aColor;
            layout (location = 3) in float aTextured;
            out vec2 vUv;
            out vec4 vColor;
            out float vTextured;
            void main() {
                gl_Position = vec4(aPosition, 0.0, 1.0);
                vUv = aUv;
                vColor = aColor;
                vTextured = aTextured;
            }
            """;
        const string fragment = """
            #version 330 core
            uniform sampler2D uSourceTexture;
            in vec2 vUv;
            in vec4 vColor;
            in float vTextured;
            out vec4 FragColor;
            void main() {
                if (vTextured > 0.5) {
                    FragColor = vColor * texture(uSourceTexture, vUv);
                } else {
                    FragColor = vColor;
                }
            }
            """;

        _shader = new ShaderProgram(vertex, fragment);
        _sourceTextureLocation = _shader.GetUniformLocation("uSourceTexture");
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, FloatsPerVertex * sizeof(float), 0);
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, FloatsPerVertex * sizeof(float), 2 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, FloatsPerVertex * sizeof(float), 4 * sizeof(float));
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, FloatsPerVertex * sizeof(float), 8 * sizeof(float));
        GL.BindVertexArray(0);
    }

    public void Render(IEnumerable<GameObject> gameObjects, int width, int height, int targetFramebuffer, bool clearTarget,
        Vector2 pointerPosition, bool inputEnabled)
    {
        if (_disposed)
        {
            return;
        }

        width = Math.Max(width, 1);
        height = Math.Max(height, 1);
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, targetFramebuffer);
        GL.Viewport(0, 0, width, height);
        if (clearTarget)
        {
            GL.ClearColor(0.1f, 0.105f, 0.11f, 1f);
            GL.Clear(ClearBufferMask.ColorBufferBit);
        }

        IReadOnlyList<UIElement> elements = CanvasLayout.Build(gameObjects, width, height);

        _events.Update(inputEnabled ? elements : Array.Empty<UIElement>(), pointerPosition,
            inputEnabled && Input.WasMouseButtonPressed(MouseButton.Left),
            inputEnabled && Input.IsMouseButtonDown(MouseButton.Left),
            inputEnabled && Input.WasMouseButtonReleased(MouseButton.Left));

        if (_fontSource is not null)
        {
            foreach (UIElement element in elements)
            {
                Text? textStyle = element.Text ?? (element.InputField is not null ? DefaultInputFieldText : null);
                _fontSource.EnsureFont(textStyle?.FontName);
            }
        }

        List<UIBatch> batches = [];
        foreach (UIElement element in elements)
        {
            if (element.Image is { } image)
            {
                Vector3 imageColor = image.Color;
                if (element.Button is { } button)
                {
                    imageColor *= _events.GetTint(button);
                }
                else if (element.Toggle is { } toggle)
                {
                    imageColor *= _events.GetTint(toggle);
                }

                if (image.Alpha > 0f)
                {
                    ResolvedSprite sprite = SpriteAssetLoader.Resolve(image);
                    int textureHandle = GetTextureHandle(sprite.TextureName);
                    UIBatch batch = GetBatch(batches, textureHandle, element.ClipRect);
                    Vector3 tint = Vector3.Clamp(imageColor, Vector3.Zero, Vector3.One);
                    float alpha = Math.Clamp(image.Alpha, 0f, 1f);

                    if (textureHandle == 0)
                    {
                        AppendSolidRect(batch.Vertices, element.Rect, width, height, tint, alpha);
                    }
                    else
                    {
                        Vector2 uvMin = Vector2.Clamp(sprite.UVMin, Vector2.Zero, Vector2.One);
                        Vector2 uvMax = Vector2.Clamp(sprite.UVMax, uvMin, Vector2.One);
                        // Image assets are loaded with their rows flipped for conventional 3D UVs. Screen-space
                        // top-left therefore samples the sprite's upper V edge.
                        if (sprite.SliceLeft > 0f || sprite.SliceTop > 0f || sprite.SliceRight > 0f || sprite.SliceBottom > 0f)
                        {
                            foreach (UITextureQuad quad in NineSliceLayout.Build(element.Rect, uvMin, uvMax,
                                sprite.SliceLeft, sprite.SliceTop, sprite.SliceRight, sprite.SliceBottom))
                            {
                                AppendTexturedRect(batch.Vertices, quad.Rect, quad.U0, quad.V0, quad.U1, quad.V1,
                                    width, height, tint, alpha, textured: 1f);
                            }
                        }
                        else
                        {
                            AppendTexturedRect(batch.Vertices, element.Rect,
                                uvMin.X, uvMax.Y, uvMax.X, uvMin.Y,
                                width, height, tint, alpha, textured: 1f);
                        }
                    }
                }
            }

            if (element.Slider is { } slider)
            {
                UIBatch batch = GetBatch(batches, 0, element.ClipRect);
                AppendSlider(batch.Vertices, element.Rect, slider, width, height);
            }

            if (_fontSource is not null && (element.Text is not null || element.InputField is not null))
            {
                RectF textClip = element.ClipRect is { } inheritedClip
                    ? inheritedClip.Intersect(element.Rect)
                    : element.Rect;
                UIBatch batch = GetBatch(batches, _fontSource.FontTextureHandle, textClip);
                Text textStyle = element.Text ?? DefaultInputFieldText;
                if (element.InputField is { } inputField)
                {
                    string content = String.IsNullOrEmpty(inputField.Value) ? inputField.Placeholder : inputField.Value;
                    Vector3? color = String.IsNullOrEmpty(inputField.Value) ? inputField.PlaceholderColor : null;
                    int? caret = ReferenceEquals(_events.FocusedInputField, inputField) ? inputField.CaretPosition : null;
                    AppendText(batch.Vertices, element, textStyle, width, height, content, color, caret, forceSingleLine: true);
                }
                else
                {
                    AppendText(batch.Vertices, element, textStyle, width, height);
                }
            }

            if (_events.FocusedControl is { } focusedControl &&
                (ReferenceEquals(focusedControl, element.Button)
                    || ReferenceEquals(focusedControl, element.Toggle)
                    || ReferenceEquals(focusedControl, element.Slider)
                    || ReferenceEquals(focusedControl, element.InputField)))
            {
                UIBatch batch = GetBatch(batches, 0, element.ClipRect);
                AppendFocusOutline(batch.Vertices, element.Rect, width, height);
            }
        }

        if (batches.All(batch => batch.Vertices.Count == 0))
        {
            return;
        }

        GL.Disable(EnableCap.DepthTest);
        GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.Blend);
        GL.BlendEquation(BlendEquationMode.FuncAdd);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.UseProgram(_shader.Handle);
        GL.Uniform1(_sourceTextureLocation, 0);
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

        foreach (UIBatch batch in batches)
        {
            if (batch.Vertices.Count == 0)
            {
                continue;
            }

            if (batch.ClipRect is { } clip)
            {
                SetScissor(clip, width, height);
                GL.Enable(EnableCap.ScissorTest);
            }
            else
            {
                GL.Disable(EnableCap.ScissorTest);
            }

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, batch.TextureHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, sizeof(float) * batch.Vertices.Count,
                batch.Vertices.ToArray(), BufferUsageHint.DynamicDraw);
            GL.DrawArrays(PrimitiveType.Triangles, 0, batch.Vertices.Count / FloatsPerVertex);
        }

        GL.Disable(EnableCap.ScissorTest);
        GL.BindVertexArray(0);
        GL.UseProgram(0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.Disable(EnableCap.Blend);
    }

    private int GetTextureHandle(string textureName)
    {
        if (_resources is null || String.IsNullOrWhiteSpace(textureName))
        {
            return 0;
        }

        if (_textureHandles.TryGetValue(textureName, out int cached))
        {
            return cached;
        }

        int resolved = 0;
        try
        {
            object handle = _resources.GetTexture(textureName).GetBackendHandle();
            resolved = handle is int textureHandle ? textureHandle : 0;
        }
        catch
        {
            // An invalid asset should fail closed to the tinted solid image, not take down the render loop.
        }

        _textureHandles[textureName] = resolved;
        return resolved;
    }

    private static UIBatch GetBatch(List<UIBatch> batches, int textureHandle, RectF? clipRect)
    {
        if (batches.Count > 0
            && batches[^1].TextureHandle == textureHandle
            && batches[^1].ClipRect == clipRect)
        {
            return batches[^1];
        }

        UIBatch batch = new(textureHandle, clipRect);
        batches.Add(batch);
        return batch;
    }

    private static void SetScissor(RectF clip, int viewportWidth, int viewportHeight)
    {
        int left = Math.Clamp((int)MathF.Floor(clip.X), 0, viewportWidth);
        int top = Math.Clamp((int)MathF.Floor(clip.Y), 0, viewportHeight);
        int right = Math.Clamp((int)MathF.Ceiling(clip.Right), 0, viewportWidth);
        int bottom = Math.Clamp((int)MathF.Ceiling(clip.Bottom), 0, viewportHeight);
        GL.Scissor(left, viewportHeight - bottom, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static void AppendFocusOutline(List<float> vertices, RectF rect, int viewportWidth, int viewportHeight)
    {
        float thickness = MathF.Min(2f, MathF.Min(rect.Width, rect.Height));
        if (thickness <= 0f)
        {
            return;
        }

        AppendSolidRect(vertices, new RectF(rect.X, rect.Y, rect.Width, thickness),
            viewportWidth, viewportHeight, FocusOutlineColor, 1f);
        AppendSolidRect(vertices, new RectF(rect.X, rect.Bottom - thickness, rect.Width, thickness),
            viewportWidth, viewportHeight, FocusOutlineColor, 1f);
        AppendSolidRect(vertices, new RectF(rect.X, rect.Y + thickness, thickness, MathF.Max(0f, rect.Height - thickness * 2f)),
            viewportWidth, viewportHeight, FocusOutlineColor, 1f);
        AppendSolidRect(vertices, new RectF(rect.Right - thickness, rect.Y + thickness, thickness, MathF.Max(0f, rect.Height - thickness * 2f)),
            viewportWidth, viewportHeight, FocusOutlineColor, 1f);
    }

    private static void AppendSlider(List<float> vertices, RectF rect, Slider slider, int viewportWidth, int viewportHeight)
    {
        float handleSize = Math.Clamp(slider.HandleSize, 0f, MathF.Max(0f, MathF.Min(rect.Width, rect.Height)));
        float thickness = Math.Clamp(slider.TrackThickness, 0f,
            slider.Direction == SliderDirection.Horizontal ? rect.Height : rect.Width);
        float normalized = slider.NormalizedValue;
        Vector3 trackColor = Vector3.Clamp(slider.Interactable ? slider.TrackColor : slider.DisabledColor, Vector3.Zero, Vector3.One);
        Vector3 fillColor = Vector3.Clamp(slider.Interactable ? slider.FillColor : slider.DisabledColor, Vector3.Zero, Vector3.One);
        Vector3 handleColor = Vector3.Clamp(slider.Interactable ? slider.HandleColor : slider.DisabledColor, Vector3.Zero, Vector3.One);

        if (slider.Direction == SliderDirection.Vertical)
        {
            float travel = MathF.Max(0f, rect.Height - handleSize);
            float topCenter = rect.Y + handleSize * 0.5f;
            float bottomCenter = rect.Bottom - handleSize * 0.5f;
            float centerY = bottomCenter - travel * normalized;
            float centerX = rect.X + rect.Width * 0.5f;
            RectF track = new(centerX - thickness * 0.5f, topCenter, thickness, MathF.Max(0f, bottomCenter - topCenter));
            RectF fill = new(centerX - thickness * 0.5f, centerY, thickness, MathF.Max(0f, bottomCenter - centerY));
            RectF handle = new(centerX - handleSize * 0.5f, centerY - handleSize * 0.5f, handleSize, handleSize);
            AppendSolidRect(vertices, track, viewportWidth, viewportHeight, trackColor, 1f);
            AppendSolidRect(vertices, fill, viewportWidth, viewportHeight, fillColor, 1f);
            AppendSolidRect(vertices, handle, viewportWidth, viewportHeight, handleColor, 1f);
        }
        else
        {
            float travel = MathF.Max(0f, rect.Width - handleSize);
            float leftCenter = rect.X + handleSize * 0.5f;
            float rightCenter = rect.Right - handleSize * 0.5f;
            float centerX = leftCenter + travel * normalized;
            float centerY = rect.Y + rect.Height * 0.5f;
            RectF track = new(leftCenter, centerY - thickness * 0.5f, MathF.Max(0f, rightCenter - leftCenter), thickness);
            RectF fill = new(leftCenter, centerY - thickness * 0.5f, MathF.Max(0f, centerX - leftCenter), thickness);
            RectF handle = new(centerX - handleSize * 0.5f, centerY - handleSize * 0.5f, handleSize, handleSize);
            AppendSolidRect(vertices, track, viewportWidth, viewportHeight, trackColor, 1f);
            AppendSolidRect(vertices, fill, viewportWidth, viewportHeight, fillColor, 1f);
            AppendSolidRect(vertices, handle, viewportWidth, viewportHeight, handleColor, 1f);
        }
    }

    private void AppendText(List<float> vertices, UIElement element, Text text, int viewportWidth, int viewportHeight,
        string? contentOverride = null, Vector3? colorOverride = null, int? caretIndex = null, bool forceSingleLine = false)
    {
        string content = contentOverride ?? text.Content;
        if (_fontSource is null || (String.IsNullOrEmpty(content) && !caretIndex.HasValue) || text.FontSize <= 0f || text.Alpha <= 0f)
        {
            return;
        }

        float canvasScale = element.Canvas.GetScale(viewportWidth, viewportHeight);
        float glyphScale = text.FontSize * canvasScale / MathF.Max(1f, _fontSource.GetFontPixelSize(text.FontName));
        float characterSpacing = text.CharacterSpacing * canvasScale;
        float lineSpacing = text.LineSpacing * canvasScale;
        float lineAdvance = text.FontSize * canvasScale + lineSpacing;
        IReadOnlyList<string> lines = text.Wrap && !forceSingleLine
            ? TextLayout.Wrap(content, element.Rect.Width, line => MeasureLine(line, text.FontName, glyphScale, characterSpacing))
            : content.Replace("\r", String.Empty).Split('\n');
        float[] lineWidths = lines.Select(line => MeasureLine(line, text.FontName, glyphScale, characterSpacing)).ToArray();
        float blockHeight = lines.Count == 0 ? 0f : text.FontSize * canvasScale + (lines.Count - 1) * lineAdvance;
        float verticalOffset = text.VerticalAlignment switch
        {
            VerticalTextAlignment.Middle => (element.Rect.Height - blockHeight) * 0.5f,
            VerticalTextAlignment.Bottom => element.Rect.Height - blockHeight,
            _ => 0f
        };
        Vector3 color = Vector3.Clamp(colorOverride ?? text.Color, Vector3.Zero, Vector3.One);
        float alpha = Math.Clamp(text.Alpha, 0f, 1f);

        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            float horizontalOffset = text.HorizontalAlignment switch
            {
                HorizontalTextAlignment.Center => (element.Rect.Width - lineWidths[lineIndex]) * 0.5f,
                HorizontalTextAlignment.Right => element.Rect.Width - lineWidths[lineIndex],
                _ => 0f
            };
            float cursorX = element.Rect.X + horizontalOffset;
            float lineTop = element.Rect.Y + verticalOffset + lineIndex * lineAdvance;
            string line = lines[lineIndex];

            for (int characterIndex = 0; characterIndex < line.Length; characterIndex++)
            {
                if (!_fontSource.TryGetGlyph(text.FontName, line[characterIndex], out ImGuiController.UIGlyph glyph))
                {
                    continue;
                }

                float x0 = cursorX + glyph.X0 * glyphScale;
                float y0 = lineTop + glyph.Y0 * glyphScale;
                float x1 = cursorX + glyph.X1 * glyphScale;
                float y1 = lineTop + glyph.Y1 * glyphScale;
                if (x1 > x0 && y1 > y0)
                {
                    AppendTexturedRect(vertices, new RectF(x0, y0, x1 - x0, y1 - y0),
                        glyph.U0, glyph.V0, glyph.U1, glyph.V1, viewportWidth, viewportHeight, color, alpha);
                }

                cursorX += glyph.AdvanceX * glyphScale;
                if (characterIndex < line.Length - 1)
                {
                    cursorX += characterSpacing;
                }
            }
        }

        if (caretIndex is { } requestedCaret && lines.Count > 0)
        {
            string firstLine = lines[0];
            int caret = Math.Clamp(requestedCaret, 0, firstLine.Length);
            float horizontalOffset = text.HorizontalAlignment switch
            {
                HorizontalTextAlignment.Center => (element.Rect.Width - lineWidths[0]) * 0.5f,
                HorizontalTextAlignment.Right => element.Rect.Width - lineWidths[0],
                _ => 0f
            };
            string prefix = firstLine[..caret];
            float caretX = element.Rect.X + horizontalOffset + MeasureLine(prefix, text.FontName, glyphScale, characterSpacing);
            if (caret > 0 && caret < firstLine.Length)
            {
                caretX += characterSpacing;
            }

            float caretY = element.Rect.Y + verticalOffset;
            AppendSolidRect(vertices, new RectF(caretX, caretY, MathF.Max(1f, canvasScale), text.FontSize * canvasScale),
                viewportWidth, viewportHeight, color, alpha);
        }
    }

    private float MeasureLine(string line, string? fontName, float glyphScale, float characterSpacing)
    {
        float width = 0f;
        int visibleCharacters = 0;
        foreach (char character in line)
        {
            if (_fontSource!.TryGetGlyph(fontName, character, out ImGuiController.UIGlyph glyph))
            {
                width += glyph.AdvanceX * glyphScale;
                visibleCharacters++;
            }
        }

        return width + Math.Max(0, visibleCharacters - 1) * characterSpacing;
    }

    private static void AppendSolidRect(List<float> vertices, RectF rect, int viewportWidth, int viewportHeight,
        Vector3 color, float alpha)
    {
        AppendTexturedRect(vertices, rect, 0f, 0f, 0f, 0f, viewportWidth, viewportHeight, color, alpha, 0f);
    }

    private static void AppendTexturedRect(List<float> vertices, RectF rect,
        float u0, float v0, float u1, float v1, int viewportWidth, int viewportHeight,
        Vector3 color, float alpha, float textured = 1f)
    {
        float x0 = rect.X / viewportWidth * 2f - 1f;
        float x1 = rect.Right / viewportWidth * 2f - 1f;
        float y0 = 1f - rect.Y / viewportHeight * 2f;
        float y1 = 1f - rect.Bottom / viewportHeight * 2f;

        AddVertex(vertices, x0, y0, u0, v0, color, alpha, textured);
        AddVertex(vertices, x0, y1, u0, v1, color, alpha, textured);
        AddVertex(vertices, x1, y1, u1, v1, color, alpha, textured);
        AddVertex(vertices, x0, y0, u0, v0, color, alpha, textured);
        AddVertex(vertices, x1, y1, u1, v1, color, alpha, textured);
        AddVertex(vertices, x1, y0, u1, v0, color, alpha, textured);
    }

    private static void AddVertex(List<float> target, float x, float y, float u, float v,
        Vector3 color, float alpha, float textured)
    {
        target.Add(x);
        target.Add(y);
        target.Add(u);
        target.Add(v);
        target.Add(color.X);
        target.Add(color.Y);
        target.Add(color.Z);
        target.Add(alpha);
        target.Add(textured);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        GL.DeleteBuffer(_vbo);
        GL.DeleteVertexArray(_vao);
        _shader.Dispose();
        _disposed = true;
    }
}
