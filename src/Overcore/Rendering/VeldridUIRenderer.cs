using Overcore;
using Overcore.Components;
using Overcore.Rendering;
using Overcore.Rendering.UI;
using System.Numerics;
using System.Text;
using Veldrid;
using Veldrid.SPIRV;

namespace Overcore.Core;

/// <summary>Veldrid screen-space renderer for image graphics, controls, clipping, and pointer/keyboard actions.</summary>
internal sealed class VeldridUIRenderer : IDisposable
{
    private const int MaxRoundedMasks = 32;
    private const int MaxAlphaMasks = 8;
    private const int RoundedMaskUniformFloatCount = MaxRoundedMasks * 8 + 4;

    private readonly record struct Vertex(Vector2 Position, Vector2 UV, Vector4 Color, Vector2 ScreenPosition);

    private sealed class Batch(ITexture texture, RectF? clipRect, IReadOnlyList<RoundedRectClip>? roundedClips)
    {
        public ITexture Texture { get; } = texture;
        public RectF? ClipRect { get; } = clipRect;
        public IReadOnlyList<RoundedRectClip> RoundedClips { get; } = roundedClips ?? Array.Empty<RoundedRectClip>();
        public List<Vertex> Vertices { get; } = [];
    }

    private readonly GraphicsDevice _device;
    private readonly IResourceManager _resources;
    private readonly UIEventSystem _events = new();
    private readonly VeldridFontAtlas _fontAtlas;
    private readonly Dictionary<string, ITexture> _resolvedTextures = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<UIElement>? _preparedElements;
    private ITexture? _whiteTexture;
    private int _preparedWidth;
    private int _preparedHeight;
    private readonly ResourceLayout _textureLayout;
    private readonly Sampler _sampler;
    private readonly DeviceBuffer _roundedMaskData;
    private DeviceBuffer _vertices;
    private readonly Shader _vertexShader;
    private readonly Shader _fragmentShader;
    private Pipeline? _pipeline;
    private bool _disposed;

    private static readonly Vector3 FocusOutlineColor = new(0.45f, 0.76f, 1f);
    private static readonly Text DefaultInputText = new() { FontSize = 24f, Wrap = false };

    public VeldridUIRenderer(GraphicsDevice device, IResourceManager resources)
    {
        _device = device;
        _resources = resources;
        _fontAtlas = new VeldridFontAtlas(device);
        ResourceFactory factory = device.ResourceFactory;
        ResourceLayoutElementDescription[] layoutElements =
        [
            new("SourceTexture", ResourceKind.TextureReadOnly, ShaderStages.Fragment),
            new("SourceSampler", ResourceKind.Sampler, ShaderStages.Fragment),
            .. Enumerable.Range(0, MaxAlphaMasks).Select(index =>
                new ResourceLayoutElementDescription($"AlphaMask{index}", ResourceKind.TextureReadOnly, ShaderStages.Fragment)),
            new("RoundedMasks", ResourceKind.UniformBuffer, ShaderStages.Fragment)
        ];
        _textureLayout = factory.CreateResourceLayout(new ResourceLayoutDescription(layoutElements));
        _roundedMaskData = factory.CreateBuffer(new BufferDescription(
            RoundedMaskUniformFloatCount * sizeof(float), BufferUsage.UniformBuffer | BufferUsage.Dynamic));
        _sampler = factory.CreateSampler(new SamplerDescription
        {
            AddressModeU = SamplerAddressMode.Clamp,
            AddressModeV = SamplerAddressMode.Clamp,
            AddressModeW = SamplerAddressMode.Clamp,
            Filter = SamplerFilter.MinLinear_MagLinear_MipPoint
        });
        _vertices = factory.CreateBuffer(new BufferDescription(4 * 1024 * 1024, BufferUsage.VertexBuffer | BufferUsage.Dynamic));
        (_vertexShader, _fragmentShader) = CreateShaders(factory);
    }

    public void SetFramebuffer(Framebuffer framebuffer)
    {
        _pipeline?.Dispose();
        ResourceFactory factory = _device.ResourceFactory;
        VertexLayoutDescription vertexLayout = new(
            new VertexElementDescription("Position", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("UV", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2),
            new VertexElementDescription("Color", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float4),
            new VertexElementDescription("ScreenPosition", VertexElementSemantic.TextureCoordinate, VertexElementFormat.Float2));
        _pipeline = factory.CreateGraphicsPipeline(new GraphicsPipelineDescription
        {
            BlendState = BlendStateDescription.SingleAlphaBlend,
            DepthStencilState = DepthStencilStateDescription.Disabled,
            RasterizerState = new RasterizerStateDescription(
                FaceCullMode.None, PolygonFillMode.Solid, FrontFace.Clockwise, false, true),
            PrimitiveTopology = PrimitiveTopology.TriangleList,
            ResourceLayouts = [_textureLayout],
            ShaderSet = new ShaderSetDescription([vertexLayout], [_vertexShader, _fragmentShader]),
            Outputs = framebuffer.OutputDescription
        });
    }

    public void Prepare(IEnumerable<GameObject> gameObjects, int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        _preparedWidth = width;
        _preparedHeight = height;
        _preparedElements = CanvasLayout.Build(gameObjects, width, height);
        bool needsWhite = false;
        foreach (UIElement element in _preparedElements)
        {
            if (element.Image is { } image)
            {
                ResolvedSprite sprite = SpriteAssetLoader.Resolve(image);
                _ = GetImageTexture(sprite.TextureName);
                needsWhite |= String.IsNullOrWhiteSpace(sprite.TextureName);
            }
            needsWhite |= element.Slider is not null || element.Button is not null || element.Toggle is not null
                || element.InputField is not null;
            Text? textStyle = element.Text ?? (element.InputField is not null ? DefaultInputText : null);
            _fontAtlas.EnsureFont(textStyle?.FontName);
        }
        if (needsWhite) _ = GetWhiteTexture();
    }

    public void Render(CommandList commandList, IEnumerable<GameObject> gameObjects, int width, int height)
    {
        if (_disposed || _pipeline is null)
        {
            return;
        }

        width = Math.Max(1, width);
        height = Math.Max(1, height);
        IReadOnlyList<UIElement> elements = _preparedElements ?? CanvasLayout.Build(gameObjects, width, height);
        _preparedElements = null;
        width = _preparedWidth > 0 ? _preparedWidth : width;
        height = _preparedHeight > 0 ? _preparedHeight : height;
        _events.Update(elements, Input.MousePosition,
            Input.WasMouseButtonPressed(global::Overcore.MouseButton.Left),
            Input.IsMouseButtonDown(global::Overcore.MouseButton.Left),
            Input.WasMouseButtonReleased(global::Overcore.MouseButton.Left));

        List<Batch> batches = [];
        foreach (UIElement element in elements)
        {
            if (element.Image is { } image && image.Alpha > 0f)
            {
                Vector3 tint = image.Color;
                if (element.Button is { } button) tint *= _events.GetTint(button);
                else if (element.Toggle is { } toggle) tint *= _events.GetTint(toggle);
                tint = Vector3.Clamp(tint, Vector3.Zero, Vector3.One);
                float alpha = Math.Clamp(image.Alpha, 0f, 1f);
                ResolvedSprite sprite = SpriteAssetLoader.Resolve(image);
                ITexture texture = GetImageTexture(sprite.TextureName);
                Batch batch = GetBatch(batches, texture, element.ClipRect, element.RoundedClips);
                if (String.IsNullOrWhiteSpace(sprite.TextureName) || ReferenceEquals(texture, GetWhiteTexture()))
                {
                    AppendQuad(batch.Vertices, element.Rect, 0f, 1f, 1f, 0f, width, height,
                        new Vector4(tint, alpha));
                }
                else
                {
                    Vector2 uvMin = Vector2.Clamp(sprite.UVMin, Vector2.Zero, Vector2.One);
                    Vector2 uvMax = Vector2.Clamp(sprite.UVMax, uvMin, Vector2.One);
                    if (sprite.SliceLeft > 0f || sprite.SliceTop > 0f || sprite.SliceRight > 0f || sprite.SliceBottom > 0f)
                    {
                        foreach (UITextureQuad quad in NineSliceLayout.Build(element.Rect, uvMin, uvMax,
                            sprite.SliceLeft, sprite.SliceTop, sprite.SliceRight, sprite.SliceBottom))
                        {
                            AppendQuad(batch.Vertices, quad.Rect, quad.U0, quad.V0, quad.U1, quad.V1,
                                width, height, new Vector4(tint, alpha));
                        }
                    }
                    else
                    {
                        AppendQuad(batch.Vertices, element.Rect, uvMin.X, uvMax.Y, uvMax.X, uvMin.Y,
                            width, height, new Vector4(tint, alpha));
                    }
                }
            }

            if (element.Slider is { } slider)
            {
                Batch batch = GetBatch(batches, GetWhiteTexture(), element.ClipRect, element.RoundedClips);
                AppendSlider(batch.Vertices, element.Rect, slider, width, height);
            }

            if (element.Text is not null || element.InputField is not null)
            {
                Text style = element.Text ?? DefaultInputText;
                string? content = null;
                Vector3? color = null;
                int? caret = null;
                bool singleLine = false;
                if (element.InputField is { } inputField)
                {
                    content = String.IsNullOrEmpty(inputField.Value) ? inputField.Placeholder : inputField.Value;
                    color = String.IsNullOrEmpty(inputField.Value) ? inputField.PlaceholderColor : null;
                    caret = ReferenceEquals(_events.FocusedInputField, inputField) ? inputField.CaretPosition : null;
                    singleLine = true;
                }
                RectF textClip = element.ClipRect is { } inherited ? inherited.Intersect(element.Rect) : element.Rect;
                Batch textBatch = GetBatch(batches, _fontAtlas.Texture, textClip, element.RoundedClips);
                RectF? caretRect = AppendText(textBatch.Vertices, element, style, width, height, content, color, caret, singleLine);
                if (caretRect is { } caretBounds)
                {
                    Batch caretBatch = GetBatch(batches, GetWhiteTexture(), textClip, element.RoundedClips);
                    AppendSolid(caretBatch.Vertices, caretBounds, width, height, color ?? style.Color);
                }
            }

            if (_events.FocusedControl is { } focused &&
                (ReferenceEquals(focused, element.Button) || ReferenceEquals(focused, element.Toggle)
                    || ReferenceEquals(focused, element.Slider) || ReferenceEquals(focused, element.InputField)))
            {
                Batch batch = GetBatch(batches, GetWhiteTexture(), element.ClipRect, element.RoundedClips);
                AppendFocusOutline(batch.Vertices, element.Rect, width, height);
            }
        }

        foreach (Batch batch in batches)
        {
            if (batch.RoundedClips.Count > 0)
            {
                UIClipGeometry.ClipTriangles(batch.Vertices, batch.RoundedClips,
                    vertex => vertex.ScreenPosition,
                    (left, right, amount) => new Vertex(
                        Vector2.Zero,
                        Vector2.Lerp(left.UV, right.UV, amount),
                        Vector4.Lerp(left.Color, right.Color, amount),
                        Vector2.Lerp(left.ScreenPosition, right.ScreenPosition, amount)));

                for (int index = 0; index < batch.Vertices.Count; index++)
                {
                    Vertex vertex = batch.Vertices[index];
                    Vector2 normalizedPosition = new(vertex.ScreenPosition.X / width * 2f - 1f,
                        1f - vertex.ScreenPosition.Y / height * 2f);
                    batch.Vertices[index] = new Vertex(normalizedPosition, vertex.UV, vertex.Color, vertex.ScreenPosition);
                }
            }
        }

        EnsureVertexCapacity(batches);
        foreach (Batch batch in batches)
        {
            if (batch.Vertices.Count == 0)
            {
                continue;
            }

            Vertex[] data = [.. batch.Vertices];
            commandList.UpdateBuffer(_vertices, 0, data);
            (float[] maskUniforms, ITexture[] alphaMasks) = BuildRoundedMaskUniforms(batch.RoundedClips);
            commandList.UpdateBuffer(_roundedMaskData, 0, maskUniforms);
            BindableResource[] bindings = new BindableResource[MaxAlphaMasks + 3];
            bindings[0] = GetVeldridTexture(batch.Texture).VeldridTextureView;
            bindings[1] = _sampler;
            for (int index = 0; index < MaxAlphaMasks; index++)
            {
                bindings[index + 2] = GetVeldridTexture(alphaMasks[index]).VeldridTextureView;
            }
            bindings[^1] = _roundedMaskData;
            ResourceSet textureSet = _device.ResourceFactory.CreateResourceSet(new ResourceSetDescription(_textureLayout, bindings));
            commandList.SetPipeline(_pipeline);
            commandList.SetVertexBuffer(0, _vertices);
            commandList.SetGraphicsResourceSet(0, textureSet);
            SetScissor(commandList, batch.ClipRect, width, height);
            commandList.Draw((uint)data.Length, 1, 0, 0);
            textureSet.Dispose();
        }
    }

    private (float[] Uniforms, ITexture[] AlphaMasks) BuildRoundedMaskUniforms(IReadOnlyList<RoundedRectClip> clips)
    {
        float[] data = new float[RoundedMaskUniformFloatCount];
        ITexture whiteTexture = GetWhiteTexture();
        ITexture[] alphaMasks = Enumerable.Repeat(whiteTexture, MaxAlphaMasks).ToArray();
        int count = Math.Min(clips.Count, MaxRoundedMasks);
        int alphaMaskCount = 0;
        for (int index = 0; index < count; index++)
        {
            RoundedRectClip clip = clips[index];
            int rectOffset = index * 4;
            data[rectOffset] = clip.Rect.X;
            data[rectOffset + 1] = clip.Rect.Y;
            data[rectOffset + 2] = clip.Rect.Width;
            data[rectOffset + 3] = clip.Rect.Height;
            int parameterOffset = MaxRoundedMasks * 4 + index * 4;
            data[parameterOffset] = clip.Radius;
            data[parameterOffset + 1] = clip.Feather;
            if (alphaMaskCount < MaxAlphaMasks && !String.IsNullOrWhiteSpace(clip.MaskTextureName))
            {
                ITexture maskTexture = GetImageTexture(clip.MaskTextureName);
                if (!ReferenceEquals(maskTexture, whiteTexture))
                {
                    alphaMasks[alphaMaskCount] = maskTexture;
                    data[parameterOffset + 2] = ++alphaMaskCount;
                    data[parameterOffset + 3] = clip.MaskThreshold;
                }
            }
        }
        data[MaxRoundedMasks * 8] = count;
        return (data, alphaMasks);
    }

    private void EnsureVertexCapacity(IReadOnlyList<Batch> batches)
    {
        if (batches.Count == 0)
        {
            return;
        }
        int vertexSize = System.Runtime.InteropServices.Marshal.SizeOf<Vertex>();
        uint required = (uint)batches.Max(batch => batch.Vertices.Count) * (uint)vertexSize;
        if (required <= _vertices.SizeInBytes)
        {
            return;
        }

        uint capacity = _vertices.SizeInBytes;
        while (capacity < required)
        {
            capacity = checked(capacity * 2);
        }
        _vertices.Dispose();
        _vertices = _device.ResourceFactory.CreateBuffer(new BufferDescription(capacity,
            BufferUsage.VertexBuffer | BufferUsage.Dynamic));
    }

    private ITexture GetImageTexture(string textureName)
    {
        if (String.IsNullOrWhiteSpace(textureName))
        {
            return GetWhiteTexture();
        }
        if (_resolvedTextures.TryGetValue(textureName, out ITexture cached))
        {
            return cached;
        }

        ITexture texture;
        try
        {
            texture = _resources.GetTexture(textureName);
        }
        catch
        {
            texture = GetWhiteTexture();
        }
        if (!ReferenceEquals(texture, GetWhiteTexture()))
        {
            _resolvedTextures[textureName] = texture;
        }
        return texture;
    }

    private ITexture GetWhiteTexture()
    {
        if (_whiteTexture is not null) return _whiteTexture;
        try
        {
            return _whiteTexture = _resources.GetTexture("White");
        }
        catch
        {
            throw new InvalidOperationException("The Veldrid resource manager must load its built-in White texture before UI rendering.");
        }
    }

    private static global::Overcore.Texture GetVeldridTexture(ITexture texture) => texture.GetBackendHandle() as global::Overcore.Texture
        ?? throw new InvalidOperationException("Veldrid UI images require Veldrid-backed textures.");

    private static Batch GetBatch(List<Batch> batches, ITexture texture, RectF? clip,
        IReadOnlyList<RoundedRectClip>? roundedClips)
    {
        IReadOnlyList<RoundedRectClip> clips = roundedClips ?? Array.Empty<RoundedRectClip>();
        if (batches.Count > 0 && ReferenceEquals(batches[^1].Texture, texture)
            && batches[^1].ClipRect == clip && ReferenceEquals(batches[^1].RoundedClips, clips))
        {
            return batches[^1];
        }
        Batch batch = new(texture, clip, clips);
        batches.Add(batch);
        return batch;
    }

    private RectF? AppendText(List<Vertex> vertices, UIElement element, Text text, int width, int height,
        string? contentOverride, Vector3? colorOverride, int? caretIndex, bool forceSingleLine)
    {
        string content = contentOverride ?? text.Content;
        if ((String.IsNullOrEmpty(content) && !caretIndex.HasValue) || !float.IsFinite(text.FontSize)
            || text.FontSize <= 0f || !float.IsFinite(text.Alpha) || text.Alpha <= 0f)
        {
            return null;
        }

        float canvasScale = element.Canvas.GetScale(width, height);
        float glyphScale = text.FontSize * canvasScale / MathF.Max(1f, _fontAtlas.GetFontPixelSize(text.FontName));
        float characterSpacing = float.IsFinite(text.CharacterSpacing) ? text.CharacterSpacing * canvasScale : 0f;
        float lineSpacing = float.IsFinite(text.LineSpacing) ? text.LineSpacing * canvasScale : 0f;
        float lineAdvance = text.FontSize * canvasScale + lineSpacing;
        IReadOnlyList<string> lines = text.Wrap && !forceSingleLine
            ? TextLayout.Wrap(content, element.Rect.Width,
                line => MeasureLine(line, text.FontName, glyphScale, characterSpacing, text.Direction))
            : content.Replace("\r", String.Empty).Split('\n');
        VisualTextLine[] visualLines = lines.Select(line => ArabicTextLayout.ShapeLine(line, text.Direction)).ToArray();
        float[] lineWidths = visualLines.Select(line => MeasureLine(line, text.FontName, glyphScale, characterSpacing)).ToArray();
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
            float horizontalOffset = ArabicTextLayout.GetAlignmentOffset(element.Rect.Width, lineWidths[lineIndex],
                text.HorizontalAlignment, visualLines[lineIndex].IsRightToLeft);
            float cursorX = element.Rect.X + horizontalOffset;
            float lineTop = element.Rect.Y + verticalOffset + lineIndex * lineAdvance;
            int visibleGlyphs = 0;
            foreach (VisualTextCluster cluster in visualLines[lineIndex].Clusters)
            foreach (Rune codepoint in cluster.Text.EnumerateRunes())
            {
                if (!_fontAtlas.TryGetGlyph(text.FontName, codepoint, out VeldridFontAtlas.Glyph glyph)) continue;
                float x0 = cursorX + glyph.X0 * glyphScale;
                float y0 = lineTop + glyph.Y0 * glyphScale;
                float x1 = cursorX + glyph.X1 * glyphScale;
                float y1 = lineTop + glyph.Y1 * glyphScale;
                if (x1 > x0 && y1 > y0)
                {
                    AppendQuad(vertices, new RectF(x0, y0, x1 - x0, y1 - y0),
                        glyph.U0, glyph.V0, glyph.U1, glyph.V1, width, height, new Vector4(color, alpha));
                }
                if (glyph.AdvanceX > 0f)
                {
                    if (visibleGlyphs++ > 0) cursorX += characterSpacing;
                    cursorX += glyph.AdvanceX * glyphScale;
                }
            }
        }

        if (caretIndex is not { } requestedCaret || lines.Count == 0) return null;
        int caret = Math.Clamp(requestedCaret, 0, lines[0].Length);
        float caretOffset = ArabicTextLayout.GetAlignmentOffset(element.Rect.Width, lineWidths[0],
            text.HorizontalAlignment, visualLines[0].IsRightToLeft);
        float visualCaretOffset = visualLines[0].GetCaretOffset(caret,
            cluster => MeasureGlyphSequence(cluster, text.FontName, glyphScale, characterSpacing), characterSpacing);
        float caretX = element.Rect.X + caretOffset + visualCaretOffset;
        return new RectF(caretX, element.Rect.Y + verticalOffset, MathF.Max(1f, canvasScale), text.FontSize * canvasScale);
    }

    private float MeasureLine(string line, string? fontName, float glyphScale, float characterSpacing,
        TextDirection direction = TextDirection.Auto) =>
        MeasureLine(ArabicTextLayout.ShapeLine(line, direction), fontName, glyphScale, characterSpacing);

    private float MeasureLine(VisualTextLine line, string? fontName, float glyphScale, float characterSpacing)
    {
        float width = 0f;
        int visibleGlyphs = 0;
        foreach (VisualTextCluster cluster in line.Clusters)
        foreach (Rune codepoint in cluster.Text.EnumerateRunes())
        {
            if (_fontAtlas.TryGetGlyph(fontName, codepoint, out VeldridFontAtlas.Glyph glyph)
                && glyph.AdvanceX > 0f)
            {
                width += glyph.AdvanceX * glyphScale;
                visibleGlyphs++;
            }
        }
        return width + Math.Max(0, visibleGlyphs - 1) * characterSpacing;
    }

    private float MeasureGlyphSequence(string text, string? fontName, float glyphScale, float characterSpacing)
    {
        float width = 0f;
        int visibleGlyphs = 0;
        foreach (Rune codepoint in text.EnumerateRunes())
        {
            if (_fontAtlas.TryGetGlyph(fontName, codepoint, out VeldridFontAtlas.Glyph glyph)
                && glyph.AdvanceX > 0f)
            {
                if (visibleGlyphs++ > 0) width += characterSpacing;
                width += glyph.AdvanceX * glyphScale;
            }
        }
        return width;
    }

    private static void AppendSlider(List<Vertex> vertices, RectF rect, Slider slider, int width, int height)
    {
        float handle = Math.Clamp(slider.HandleSize, 0f, MathF.Max(0f, MathF.Min(rect.Width, rect.Height)));
        float thickness = Math.Clamp(slider.TrackThickness, 0f, MathF.Max(0f, MathF.Min(rect.Width, rect.Height)));
        float normalized = slider.NormalizedValue;
        Vector3 trackColor = Vector3.Clamp(slider.TrackColor, Vector3.Zero, Vector3.One);
        Vector3 fillColor = Vector3.Clamp(slider.FillColor, Vector3.Zero, Vector3.One);
        Vector3 handleColor = Vector3.Clamp(slider.HandleColor, Vector3.Zero, Vector3.One);
        if (!slider.Interactable)
        {
            trackColor *= slider.DisabledColor;
            fillColor *= slider.DisabledColor;
            handleColor *= slider.DisabledColor;
        }

        if (slider.Direction == SliderDirection.Vertical)
        {
            float top = rect.Y + handle * 0.5f;
            float bottom = rect.Bottom - handle * 0.5f;
            float center = bottom - MathF.Max(0f, bottom - top) * normalized;
            float centerX = rect.X + rect.Width * 0.5f;
            AppendSolid(vertices, new RectF(centerX - thickness * 0.5f, top, thickness, MathF.Max(0f, bottom - top)), width, height, trackColor);
            AppendSolid(vertices, new RectF(centerX - thickness * 0.5f, center, thickness, MathF.Max(0f, bottom - center)), width, height, fillColor);
            AppendSolid(vertices, new RectF(centerX - handle * 0.5f, center - handle * 0.5f, handle, handle), width, height, handleColor);
        }
        else
        {
            float left = rect.X + handle * 0.5f;
            float right = rect.Right - handle * 0.5f;
            float center = left + MathF.Max(0f, right - left) * normalized;
            float centerY = rect.Y + rect.Height * 0.5f;
            AppendSolid(vertices, new RectF(left, centerY - thickness * 0.5f, MathF.Max(0f, right - left), thickness), width, height, trackColor);
            AppendSolid(vertices, new RectF(left, centerY - thickness * 0.5f, MathF.Max(0f, center - left), thickness), width, height, fillColor);
            AppendSolid(vertices, new RectF(center - handle * 0.5f, centerY - handle * 0.5f, handle, handle), width, height, handleColor);
        }
    }

    private static void AppendFocusOutline(List<Vertex> vertices, RectF rect, int width, int height)
    {
        float thickness = MathF.Min(2f, MathF.Min(rect.Width, rect.Height));
        if (thickness <= 0f) return;
        AppendSolid(vertices, new RectF(rect.X, rect.Y, rect.Width, thickness), width, height, FocusOutlineColor);
        AppendSolid(vertices, new RectF(rect.X, rect.Bottom - thickness, rect.Width, thickness), width, height, FocusOutlineColor);
        AppendSolid(vertices, new RectF(rect.X, rect.Y + thickness, thickness, MathF.Max(0f, rect.Height - 2f * thickness)), width, height, FocusOutlineColor);
        AppendSolid(vertices, new RectF(rect.Right - thickness, rect.Y + thickness, thickness, MathF.Max(0f, rect.Height - 2f * thickness)), width, height, FocusOutlineColor);
    }

    private static void AppendSolid(List<Vertex> vertices, RectF rect, int width, int height, Vector3 color) =>
        AppendQuad(vertices, rect, 0f, 1f, 1f, 0f, width, height, new Vector4(Vector3.Clamp(color, Vector3.Zero, Vector3.One), 1f));

    private static void AppendQuad(List<Vertex> vertices, RectF rect, float u0, float v0, float u1, float v1,
        int viewportWidth, int viewportHeight, Vector4 color)
    {
        Vector2 topLeft = new(rect.X / viewportWidth * 2f - 1f, 1f - rect.Y / viewportHeight * 2f);
        Vector2 topRight = new(rect.Right / viewportWidth * 2f - 1f, topLeft.Y);
        Vector2 bottomLeft = new(topLeft.X, 1f - rect.Bottom / viewportHeight * 2f);
        Vector2 bottomRight = new(topRight.X, bottomLeft.Y);
        Vertex a = new(topLeft, new Vector2(u0, v0), color, new Vector2(rect.X, rect.Y));
        Vertex b = new(topRight, new Vector2(u1, v0), color, new Vector2(rect.Right, rect.Y));
        Vertex c = new(bottomRight, new Vector2(u1, v1), color, new Vector2(rect.Right, rect.Bottom));
        Vertex d = new(bottomLeft, new Vector2(u0, v1), color, new Vector2(rect.X, rect.Bottom));
        vertices.Add(a); vertices.Add(b); vertices.Add(c);
        vertices.Add(a); vertices.Add(c); vertices.Add(d);
    }

    private static void SetScissor(CommandList commandList, RectF? clip, int width, int height)
    {
        if (clip is not { } rect)
        {
            commandList.SetScissorRect(0, 0, 0, (uint)width, (uint)height);
            return;
        }
        uint left = (uint)Math.Clamp((int)MathF.Floor(rect.X), 0, width);
        uint top = (uint)Math.Clamp((int)MathF.Floor(rect.Y), 0, height);
        uint right = (uint)Math.Clamp((int)MathF.Ceiling(rect.Right), 0, width);
        uint bottom = (uint)Math.Clamp((int)MathF.Ceiling(rect.Bottom), 0, height);
        commandList.SetScissorRect(0, left, top, right - left, bottom - top);
    }

    private static (Shader Vertex, Shader Fragment) CreateShaders(ResourceFactory factory)
    {
        const string vertex = """
            #version 450
            layout(location = 0) in vec2 Position;
            layout(location = 1) in vec2 UV;
            layout(location = 2) in vec4 Color;
            layout(location = 3) in vec2 ScreenPosition;
            layout(location = 0) out vec2 fsin_UV;
            layout(location = 1) out vec4 fsin_Color;
            layout(location = 2) out vec2 fsin_ScreenPosition;
            void main() {
                gl_Position = vec4(Position, 0.0, 1.0);
                fsin_UV = UV;
                fsin_Color = Color;
                fsin_ScreenPosition = ScreenPosition;
            }
            """;
        const string fragment = """
            #version 450
            layout(set = 0, binding = 0) uniform texture2D SourceTexture;
            layout(set = 0, binding = 1) uniform sampler SourceSampler;
            layout(set = 0, binding = 2) uniform texture2D AlphaMask0;
            layout(set = 0, binding = 3) uniform texture2D AlphaMask1;
            layout(set = 0, binding = 4) uniform texture2D AlphaMask2;
            layout(set = 0, binding = 5) uniform texture2D AlphaMask3;
            layout(set = 0, binding = 6) uniform texture2D AlphaMask4;
            layout(set = 0, binding = 7) uniform texture2D AlphaMask5;
            layout(set = 0, binding = 8) uniform texture2D AlphaMask6;
            layout(set = 0, binding = 9) uniform texture2D AlphaMask7;
            layout(set = 0, binding = 10, std140) uniform RoundedMaskBlock {
                vec4 MaskRects[32];
                vec4 MaskParams[32];
                vec4 MaskInfo;
            } RoundedMasks;
            layout(location = 0) in vec2 fsin_UV;
            layout(location = 1) in vec4 fsin_Color;
            layout(location = 2) in vec2 fsin_ScreenPosition;
            layout(location = 0) out vec4 fsout_Color;
            float roundedRectDistance(vec2 point, vec4 rect, float radius) {
                vec2 halfSize = rect.zw * 0.5;
                vec2 center = rect.xy + halfSize;
                vec2 q = abs(point - center) - (halfSize - vec2(radius));
                return length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - radius;
            }
            vec2 clampMaskUv(vec2 uv, ivec2 textureSizeInPixels) {
                vec2 halfTexel = 0.5 / vec2(textureSizeInPixels);
                return clamp(uv, halfTexel, vec2(1.0) - halfTexel);
            }
            float sampleAlphaMask(int index, vec2 uv) {
                switch (index) {
                    case 0: return textureLod(sampler2D(AlphaMask0, SourceSampler), clampMaskUv(uv, textureSize(sampler2D(AlphaMask0, SourceSampler), 0)), 0.0).a;
                    case 1: return textureLod(sampler2D(AlphaMask1, SourceSampler), clampMaskUv(uv, textureSize(sampler2D(AlphaMask1, SourceSampler), 0)), 0.0).a;
                    case 2: return textureLod(sampler2D(AlphaMask2, SourceSampler), clampMaskUv(uv, textureSize(sampler2D(AlphaMask2, SourceSampler), 0)), 0.0).a;
                    case 3: return textureLod(sampler2D(AlphaMask3, SourceSampler), clampMaskUv(uv, textureSize(sampler2D(AlphaMask3, SourceSampler), 0)), 0.0).a;
                    case 4: return textureLod(sampler2D(AlphaMask4, SourceSampler), clampMaskUv(uv, textureSize(sampler2D(AlphaMask4, SourceSampler), 0)), 0.0).a;
                    case 5: return textureLod(sampler2D(AlphaMask5, SourceSampler), clampMaskUv(uv, textureSize(sampler2D(AlphaMask5, SourceSampler), 0)), 0.0).a;
                    case 6: return textureLod(sampler2D(AlphaMask6, SourceSampler), clampMaskUv(uv, textureSize(sampler2D(AlphaMask6, SourceSampler), 0)), 0.0).a;
                    case 7: return textureLod(sampler2D(AlphaMask7, SourceSampler), clampMaskUv(uv, textureSize(sampler2D(AlphaMask7, SourceSampler), 0)), 0.0).a;
                }
                return 1.0;
            }
            void main() {
                vec4 color = fsin_Color * texture(sampler2D(SourceTexture, SourceSampler), fsin_UV);
                int maskCount = int(RoundedMasks.MaskInfo.x + 0.5);
                for (int index = 0; index < maskCount; index++) {
                    float feather = RoundedMasks.MaskParams[index].y;
                    if (feather > 0.0) {
                        float distanceToEdge = roundedRectDistance(fsin_ScreenPosition,
                            RoundedMasks.MaskRects[index], RoundedMasks.MaskParams[index].x);
                        color.a *= 1.0 - smoothstep(-feather, 0.0, distanceToEdge);
                    }
                    float alphaMaskSlot = RoundedMasks.MaskParams[index].z;
                    if (alphaMaskSlot > 0.0) {
                        vec2 maskUv = (fsin_ScreenPosition - RoundedMasks.MaskRects[index].xy)
                            / RoundedMasks.MaskRects[index].zw;
                        maskUv.y = 1.0 - maskUv.y;
                        float maskAlpha = sampleAlphaMask(int(alphaMaskSlot + 0.5) - 1, maskUv);
                        color.a *= maskAlpha;
                    }
                }
                fsout_Color = color;
            }
            """;
        Shader[] shaders = factory.CreateFromSpirv(
            new ShaderDescription(ShaderStages.Vertex, System.Text.Encoding.UTF8.GetBytes(vertex), "main"),
            new ShaderDescription(ShaderStages.Fragment, System.Text.Encoding.UTF8.GetBytes(fragment), "main"));
        return (shaders[0], shaders[1]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pipeline?.Dispose();
        _vertexShader.Dispose();
        _fragmentShader.Dispose();
        _textureLayout.Dispose();
        _sampler.Dispose();
        _roundedMaskData.Dispose();
        _vertices.Dispose();
        _fontAtlas.Dispose();
    }
}
