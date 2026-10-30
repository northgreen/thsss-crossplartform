using System;
using System.Collections.Generic;
using System.Numerics;
using Silk.NET.OpenGL;
using THSSS.Graphics;
using THSSS.Graphics.OpenGl;

namespace SlimDX.Direct3D9;

internal sealed class Device3D : IDisposable
{
    private readonly Device _device;
    private readonly GL _gl;
    private uint _program;
    private IRenderShader _shader;
    private uint _vao;
    private int _worldLocation, _viewLocation, _projectionLocation, _textureLocation, _colourLocation;
    /// <summary>Attribute presence computed by BuildLayout for the current draw.</summary>
    private (bool HasColour, bool PackedColour, bool HasNormal, bool HasSecondary, bool PackedSecondary) AttributePresence;
    private THSSS.Graphics.IRenderBuffer _projBlock;
    private readonly Matrix4x4[] _transforms = new Matrix4x4[4];
    private VertexBuffer _stream;
    private VertexDeclaration _declaration;
    private Material _material;
    private Texture _texture0;
    private bool _depthTest;
    private Cull _cull = Cull.None;
    private bool _fogEnabled, _lighting, _specularEnabled;
    private bool _localViewer = true;
    private bool _normalizeNormals;
    private bool _colorVertex = true;
    private int _diffuseSource = 1, _specularSource = 2, _ambientSource, _emissiveSource;
    private float _fogStart, _fogEnd = 1, _fogDensity = 1;
    private FogMode _fogMode;
    private System.Drawing.Color _fogColour = System.Drawing.Color.Black;
    private readonly System.Collections.Generic.Dictionary<int, Light> _lights = new();
    private readonly System.Collections.Generic.HashSet<int> _enabledLights = new();
    private System.Drawing.Color _ambient = System.Drawing.Color.Black;
    private int _diagnosticDraw;

    internal Device3D(Device device, GL gl)
    {
        _device = device;
        _gl = gl;
        for (int i = 0; i < _transforms.Length; i++) _transforms[i] = Matrix4x4.Identity;
        CreateProgram();

    }

    internal static ShaderSource CreateShaderSource()
    {
        const string vertex = @"#version 330 core
layout(location=0) in vec3 aPosition;
layout(location=1) in vec2 aTexCoord;
layout(location=2) in vec3 aNormal;
layout(location=3) in vec4 aColour;
layout(location=4) in vec4 aSecondary;
out vec4 VertexColour;
out vec3 VertexSpecular;
layout (std140) uniform ProjectionBlock { mat4 uProjection; vec4 uColour; int uFogMode; mat4 uWorld; mat4 uView; int uLighting; int uHasColour; int uPackedColour; int uHasNormal; int uHasSecondary; int uPackedSecondary; int uColorVertex; int uNormalizeNormals; int uSpecularEnabled; int uDiffuseSource; int uAmbientSource; int uEmissiveSource; int uSpecularSource; int uLocalViewer; int uLightCount; vec3 uAmbientLight; vec3 uSpecular; float uPower; vec3 uFogColour; vec3 uFogParams; vec4 uMaterialAmbient; vec4 uMaterialEmissive; vec3 uLightDirection[4]; vec3 uLightDiffuse[4]; vec3 uLightSpecular[4]; };
out vec2 TexCoord;
out float FogDepth;
vec4 sourceColour(int source, vec4 material, vec4 primary, vec4 secondary) {
    if (uColorVertex != 0 && source == 1 && uHasColour != 0) return primary;
    if (uColorVertex != 0 && source == 2 && uHasSecondary != 0) return secondary;
    return material;
}
void main() {
    vec4 eye = uView * uWorld * vec4(aPosition, 1.0);
    FogDepth = abs(eye.z);
    vec4 clip = uProjection * eye;
    clip.z = 2.0 * clip.z - clip.w;
    gl_Position = clip;
    TexCoord = aTexCoord;
    vec4 vertex = uPackedColour != 0 ? aColour.bgra : aColour;
    vec4 secondary = uPackedSecondary != 0 ? aSecondary.bgra : aSecondary;
    VertexColour = uHasColour != 0 ? vertex : uColour;
    VertexSpecular = uSpecularEnabled != 0 && uHasSecondary != 0 ? secondary.rgb : vec3(0);
    if (uLighting != 0) {
        vec4 diffuse = sourceColour(uDiffuseSource, uColour, vertex, secondary);
        vec3 ambient = sourceColour(uAmbientSource, uMaterialAmbient, vertex, secondary).rgb;
        vec3 emission = sourceColour(uEmissiveSource, uMaterialEmissive, vertex, secondary).rgb;
        vec3 specular = sourceColour(uSpecularSource, vec4(uSpecular,1.0), vertex, secondary).rgb;
        vec3 lit = ambient * uAmbientLight + emission;
        VertexSpecular = vec3(0);
        if (uHasNormal != 0) {
            vec3 normal = transpose(inverse(mat3(uView * uWorld))) * aNormal;
            if (uNormalizeNormals != 0 && dot(normal,normal) > 0.0) normal = normalize(normal);
            for (int i=0; i<uLightCount; i++) {
                vec3 light = normalize(-uLightDirection[i]);
                float amount = max(dot(normal, light), 0.0);
                lit += diffuse.rgb * uLightDiffuse[i] * amount;
                if (amount > 0.0 && uPower > 0.0) {
                    vec3 viewer = uLocalViewer != 0 ? normalize(-eye.xyz) : vec3(0,0,-1);
                    vec3 halfVector = normalize(light + viewer);
                    VertexSpecular += specular * uLightSpecular[i] * pow(max(dot(normal,halfVector),0.0),uPower);
                }
            }
        }
        VertexColour = vec4(clamp(lit,0.0,1.0),diffuse.a);
        VertexSpecular = clamp(VertexSpecular,0.0,1.0);
    }
}";
        const string fragment = @"#version 330 core
in vec2 TexCoord;
out vec4 FragColor;
layout (std140) uniform ProjectionBlock { mat4 uProjection; vec4 uColour; int uFogMode; mat4 uWorld; mat4 uView; int uLighting; int uHasColour; int uPackedColour; int uHasNormal; int uHasSecondary; int uPackedSecondary; int uColorVertex; int uNormalizeNormals; int uSpecularEnabled; int uDiffuseSource; int uAmbientSource; int uEmissiveSource; int uSpecularSource; int uLocalViewer; int uLightCount; vec3 uAmbientLight; vec3 uSpecular; float uPower; vec3 uFogColour; vec3 uFogParams; vec4 uMaterialAmbient; vec4 uMaterialEmissive; vec3 uLightDirection[4]; vec3 uLightDiffuse[4]; vec3 uLightSpecular[4]; };
uniform sampler2D uTexture;
in vec4 VertexColour;
in vec3 VertexSpecular;
in float FogDepth;
void main() {
    vec4 colour = texture(uTexture, TexCoord) * VertexColour;
    colour.rgb += VertexSpecular;
    float visibility = 1.0;
    if (uFogMode == 3) visibility = clamp((uFogParams.y - FogDepth) / (uFogParams.y - uFogParams.x), 0.0, 1.0);
    if (uFogMode == 1) visibility = clamp(exp(-uFogParams.z * FogDepth), 0.0, 1.0);
    if (uFogMode == 2) visibility = clamp(exp(-pow(uFogParams.z * FogDepth, 2.0)), 0.0, 1.0);
    FragColor = vec4(mix(uFogColour, colour.rgb, visibility), colour.a);
}";
        return new ShaderSource { Vertex = vertex, Fragment = fragment, VertexInputs = new[] { "aPosition", "aTexCoord", "aNormal", "aColour", "aSecondary" } };
    }

    private void CreateProgram()
    {
        // One program per Device3D; the backend compiles and owns it.
        _shader = _device.RenderBackend.CreateShader(CreateShaderSource());
        _program = _shader.Handle is OpenGlHandle glProgram ? glProgram.Id : 0;
        _worldLocation = _shader.GetUniformLocation("uWorld");
        _viewLocation = _shader.GetUniformLocation("uView");
        _projectionLocation = _shader.GetUniformLocation("uProjection");
        _textureLocation = _shader.GetUniformLocation("uTexture");
        _colourLocation = _shader.GetUniformLocation("uColour");
    }

    internal void SetTransform(TransformState state, Matrix matrix) => _transforms[(int)state] = matrix.ToSystemMatrix();
    internal Matrix4x4 World => _transforms[(int)TransformState.World];
    internal Matrix4x4 View => _transforms[(int)TransformState.View];
    internal Matrix4x4 Projection => _transforms[(int)TransformState.Projection];
    private int _offset;
    private int _stride;
    internal void SetStreamSource(VertexBuffer buffer, int offset, int stride)
    {
        _stream = buffer;
        _offset = offset;
        _stride = stride;
    }
    internal void SetVertexDeclaration(VertexDeclaration declaration) => _declaration = declaration;
    internal void SetIndices(IndexBuffer indices) { }
    internal void SetMaterial(Material material) => _material = material;
    internal void SetTexture(Texture texture) => _texture0 = texture;
    internal void SetDepthTest(bool enabled) => _depthTest = enabled;
    internal void SetLight(int index, Light light) => _lights[index] = light;
    internal void EnableLight(int index, bool enabled) { if (enabled) _enabledLights.Add(index); else _enabledLights.Remove(index); }
    internal void SetState(RenderState state, int value)
    {
        switch (state)
        {
            case RenderState.CullMode: _cull = (Cull)value; break;
            case RenderState.FogEnable: _fogEnabled = value != 0; break;
            case RenderState.FogTableMode: _fogMode = (FogMode)value; break;
            case RenderState.FogColor: _fogColour = System.Drawing.Color.FromArgb(value); break;
            case RenderState.Lighting: _lighting = value != 0; break;
            case RenderState.SpecularEnable: _specularEnabled = value != 0; break;
            case RenderState.LocalViewer: _localViewer = value != 0; break;
            case RenderState.NormalizeNormals: _normalizeNormals = value != 0; break;
            case RenderState.ColorVertex: _colorVertex = value != 0; break;
            case RenderState.DiffuseMaterialSource: _diffuseSource = CheckedSource(value); break;
            case RenderState.SpecularMaterialSource: _specularSource = CheckedSource(value); break;
            case RenderState.AmbientMaterialSource: _ambientSource = CheckedSource(value); break;
            case RenderState.EmissiveMaterialSource: _emissiveSource = CheckedSource(value); break;
            case RenderState.Ambient: _ambient = System.Drawing.Color.FromArgb(value); break;
        }
    }
    private static int CheckedSource(int value)
        => value is >= 0 and <= 2 ? value : throw new ArgumentOutOfRangeException(nameof(value),"Material source must be Material=0, Color1=1 or Color2=2.");

    internal void SetState(RenderState state, float value)
    {
        switch (state)
        {
            case RenderState.FogStart: _fogStart = value; break;
            case RenderState.FogEnd: _fogEnd = value; break;
            case RenderState.FogDensity: _fogDensity = value; break;
        }
    }

    internal unsafe void Draw(PrimitiveType type, int startVertex, int primitiveCount)
    {
        if (_stream == null || primitiveCount <= 0) return;
        int vertexCount = type switch
        {
            PrimitiveType.PointList => primitiveCount,
            PrimitiveType.LineList => primitiveCount * 2,
            PrimitiveType.LineStrip => primitiveCount + 1,
            PrimitiveType.TriangleStrip or PrimitiveType.TriangleFan => primitiveCount + 2,
            _ => primitiveCount * 3,
        };
        var glType = type switch
        {
            PrimitiveType.PointList => Silk.NET.OpenGL.PrimitiveType.Points,
            PrimitiveType.LineList => Silk.NET.OpenGL.PrimitiveType.Lines,
            PrimitiveType.LineStrip => Silk.NET.OpenGL.PrimitiveType.LineStrip,
            PrimitiveType.TriangleStrip => Silk.NET.OpenGL.PrimitiveType.TriangleStrip,
            PrimitiveType.TriangleFan => Silk.NET.OpenGL.PrimitiveType.TriangleFan,
            _ => Silk.NET.OpenGL.PrimitiveType.Triangles,
        };
        _device.ApplyBlendState();
        _device.RenderBackend.SetCullMode(_cull switch
        {
            Cull.Clockwise => CullMode.Clockwise,
            Cull.Counterclockwise => CullMode.CounterClockwise,
            _ => CullMode.None,
        });
        _device.RenderBackend.SetDepthWrite(_device.DepthWriteEnabled);
        _device.RenderBackend.SetDepthTest(_depthTest);
        _device.RenderBackend.BindFramebuffer(_device.RenderTarget?.Framebuffer);
        _shader.Use();
        var diffuse = _material.Diffuse;
                // D3D9 samples an unset texture stage as opaque white, not as black.
        var stageTexture = _texture0?.RenderTexture ?? _device.WhiteFallbackTexture;
        _device.RenderBackend.BindTexture(stageTexture);
        _shader.SetUniform(_textureLocation, 0);
        _device.ApplySamplerState();
        int width = _device.RenderTarget?.Width ?? Math.Max(1, _device.FramebufferWidth);
        int height = _device.RenderTarget?.Height ?? Math.Max(1, _device.FramebufferHeight);
        var viewport = _device.Viewport;
        int logicalWidth = _device.RenderTarget?.Width ?? _device.GameWidth;
        int logicalHeight = _device.RenderTarget?.Height ?? _device.GameHeight;
        var content = THSSS.Graphics.PresentationGeometry.Fit(width, height, logicalWidth, logicalHeight);
        float scaleX = (float)content.Width / logicalWidth;
        float scaleY = (float)content.Height / logicalHeight;
        int left = content.X + (int)(viewport.X * scaleX);
        int bottom = content.Y + (int)((logicalHeight - viewport.Y - viewport.Height) * scaleY);
        _device.RenderBackend.SetViewport(left, bottom, (int)(viewport.Width * scaleX),
            (int)(viewport.Height * scaleY), viewport.MinZ, viewport.MaxZ);
        // The backend re-establishes attribute pointers for this buffer on every draw;
        // a VAO would cache the previously bound buffer and break re-uploads.
        int stride = _stride > 0 ? _stride : _stream.Stride;
        var layout = BuildLayout(stride);
        _projBlock ??= _device.RenderBackend.CreateBuffer(THSSS.Graphics.BufferKind.Uniform, THSSS.Graphics.ModelUniformBlock.Size);
        var projBytes = new byte[THSSS.Graphics.ModelUniformBlock.Size];
        void F(int o, float v) => BitConverter.TryWriteBytes(projBytes.AsSpan(o, 4), v);
        void I(int o, int v) => BitConverter.TryWriteBytes(projBytes.AsSpan(o, 4), v);
        void M(int o, float[] v) { for (int i = 0; i < 16; i++) F(o + i * 4, v[i]); }
        M(0, WorldViewProjection.ColumnMajor(Projection));
        var dc = _material.Diffuse;
        F(64, dc.Red); F(68, dc.Green); F(72, dc.Blue); F(76, dc.Alpha);
        I(80, _fogEnabled ? (int)_fogMode : 0);
        M(96, WorldViewProjection.ColumnMajor(World));
        M(160, WorldViewProjection.ColumnMajor(View));
        I(224, _lighting ? 1 : 0);
        I(228, AttributePresence.HasColour ? 1 : 0);
        I(232, AttributePresence.PackedColour ? 1 : 0);
        I(236, AttributePresence.HasNormal ? 1 : 0);
        I(240, AttributePresence.HasSecondary ? 1 : 0);
        I(244, AttributePresence.PackedSecondary ? 1 : 0);
        I(248, _colorVertex ? 1 : 0);
        I(252, _normalizeNormals ? 1 : 0);
        I(256, _specularEnabled ? 1 : 0);
        I(260, _diffuseSource); I(264, _ambientSource); I(268, _emissiveSource); I(272, _specularSource);
        I(276, _localViewer ? 1 : 0);
        int lightCount = 0;
        float ar = _ambient.R / 255f, ag = _ambient.G / 255f, ab = _ambient.B / 255f;
        foreach (int index in _enabledLights)
        {
            if (!_lights.TryGetValue(index, out var light)) continue;
            if (_lighting) { ar += light.Ambient.R / 255f; ag += light.Ambient.G / 255f; ab += light.Ambient.B / 255f; }
            if (light.Type != LightType.Directional || lightCount == THSSS.Graphics.ModelUniformBlock.MaxLights) continue;
            var d = System.Numerics.Vector3.TransformNormal(light.Direction, View);
            F(THSSS.Graphics.ModelUniformBlock.LightDirection + lightCount * THSSS.Graphics.ModelUniformBlock.LightStride, d.X); F(THSSS.Graphics.ModelUniformBlock.LightDirection + 4 + lightCount * THSSS.Graphics.ModelUniformBlock.LightStride, d.Y); F(THSSS.Graphics.ModelUniformBlock.LightDirection + 8 + lightCount * THSSS.Graphics.ModelUniformBlock.LightStride, d.Z);
            F(THSSS.Graphics.ModelUniformBlock.LightDiffuse + lightCount * THSSS.Graphics.ModelUniformBlock.LightStride, light.Diffuse.R / 255f); F(THSSS.Graphics.ModelUniformBlock.LightDiffuse + 4 + lightCount * THSSS.Graphics.ModelUniformBlock.LightStride, light.Diffuse.G / 255f); F(THSSS.Graphics.ModelUniformBlock.LightDiffuse + 8 + lightCount * THSSS.Graphics.ModelUniformBlock.LightStride, light.Diffuse.B / 255f);
            F(THSSS.Graphics.ModelUniformBlock.LightSpecular + lightCount * THSSS.Graphics.ModelUniformBlock.LightStride, light.Specular.R / 255f); F(THSSS.Graphics.ModelUniformBlock.LightSpecular + 4 + lightCount * THSSS.Graphics.ModelUniformBlock.LightStride, light.Specular.G / 255f); F(THSSS.Graphics.ModelUniformBlock.LightSpecular + 8 + lightCount * THSSS.Graphics.ModelUniformBlock.LightStride, light.Specular.B / 255f);
            lightCount++;
        }
        I(280, lightCount);
        F(288, ar); F(292, ag); F(296, ab);
        F(304, _material.Specular.Red); F(308, _material.Specular.Green); F(312, _material.Specular.Blue);
        F(316, _specularEnabled ? _material.Power : 0f);
        F(320, _fogColour.R / 255f); F(324, _fogColour.G / 255f); F(328, _fogColour.B / 255f);
        F(336, _fogStart); F(340, _fogEnd); F(344, _fogDensity);
        F(352, _material.Ambient.Red); F(356, _material.Ambient.Green); F(360, _material.Ambient.Blue); F(364, _material.Ambient.Alpha);
        F(368, _material.Emissive.Red); F(372, _material.Emissive.Green); F(376, _material.Emissive.Blue); F(380, _material.Emissive.Alpha);
        _device.RenderBackend.SetUniformBlock(0, _projBlock, projBytes);
        bool probe = Environment.GetEnvironmentVariable("THSSS_DIAG3D") == "1"
            && ++_diagnosticDraw >= 100 && _diagnosticDraw <= 1000 && _diagnosticDraw % 20 == 0;
        byte[] before = probe ? ReadTarget(width, height) : null;
        _device.RenderBackend.Draw(_shader, _stream.Buffer, layout,
            (int)glType == (int)PrimitiveType.TriangleStrip ? PrimitiveKind.TriangleStrip : PrimitiveKind.TriangleList,
            startVertex, vertexCount);
        if (probe)
        {
            var after = ReadTarget(width, height);
            int changed = 0;
            for (int i = 0; i < after.Length; i += 4)
                if (after[i] != before[i] || after[i+1] != before[i+1] || after[i+2] != before[i+2]) changed++;
            Console.WriteLine($"[3D-PIXELS] draw={_diagnosticDraw} changedRGB={changed} texture={_texture0?.Handle} materialAlpha={diffuse.Alpha} error={_gl.GetError()}");
        }

    }

    /// <summary>
    /// Describes the stream layout the backend must bind for this draw. Attributes are
    /// only listed when the vertex actually carries them; absent ones fall back to the
    /// default value the shader expects.
    /// </summary>
    private VertexLayout BuildLayout(int stride)
    {
        int position = 0, normal = -1, uv = -1, colour = -1, secondary = -1;
        bool packed = true, secondaryPacked = true;
        if (_declaration != null)
        {
            foreach (var element in _declaration.Elements)
            {
                if (element.Type == DeclarationType.Unused || element.Stream != 0) continue;
                switch (element.Usage)
                {
                    case DeclarationUsage.Position: position = element.Offset; break;
                    case DeclarationUsage.Normal: normal = element.Offset; break;
                    case DeclarationUsage.TexCoord when element.UsageIndex == 0: uv = element.Offset; break;
                    case DeclarationUsage.Color when element.UsageIndex == 0:
                        colour = element.Offset; packed = element.Type == DeclarationType.Color; break;
                    case DeclarationUsage.Color when element.UsageIndex == 1:
                        secondary = element.Offset; secondaryPacked = element.Type == DeclarationType.Color; break;
                }
            }
        }
        else
        {
            int offset = 12;
            var format = _device.VertexFormat;
            if ((format & VertexFormat.Normal) != 0) { normal = offset; offset += 12; }
            if ((format & VertexFormat.Diffuse) != 0) { colour = offset; offset += 4; }
            if ((format & VertexFormat.Specular) != 0) { secondary = offset; offset += 4; }
            if (offset + 8 <= stride) uv = offset;
        }

        var attributes = new List<VertexAttribute>(5)
        {
            new(0, 3, VertexAttributeType.Float, false, _offset + position),
        };
        if (uv >= 0) attributes.Add(new(1, 2, VertexAttributeType.Float, false, _offset + uv));
        if (normal >= 0) attributes.Add(new(2, 3, VertexAttributeType.Float, false, _offset + normal));
        if (colour >= 0)
            attributes.Add(new(3, 4, packed ? VertexAttributeType.UnsignedByte : VertexAttributeType.Float,
                packed, _offset + colour));
        if (secondary >= 0)
            attributes.Add(new(4, 4, secondaryPacked ? VertexAttributeType.UnsignedByte : VertexAttributeType.Float,
                secondaryPacked, _offset + secondary));

        SetUniform1("uHasNormal", normal >= 0 ? 1 : 0);
        SetUniform1("uHasColour", colour >= 0 ? 1 : 0);
        SetUniform1("uPackedColour", packed ? 1 : 0);
        SetUniform1("uHasSecondary", secondary >= 0 ? 1 : 0);
        SetUniform1("uPackedSecondary", secondaryPacked ? 1 : 0);
        AttributePresence = (colour >= 0, packed, normal >= 0, secondary >= 0, secondaryPacked);
        return new VertexLayout(stride, attributes.ToArray());
    }

    private void SetUniform1(string name, int value)
    {
        _shader.SetUniform(_shader.GetUniformLocation(name), value);
    }

    private unsafe byte[] ReadTarget(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        fixed (byte* p = pixels)
            _gl.ReadPixels(0, 0, (uint)width, (uint)height, PixelFormat.Rgba, PixelType.UnsignedByte, p);
        return pixels;
    }

    private void UploadMatrix(int location, Matrix4x4 m)
    {
        var values = WorldViewProjection.ColumnMajor(m);
        _gl.UniformMatrix4(location, 1, false, values);
    }

    public void Dispose()
    {
        _projBlock?.Dispose();
        _shader?.Dispose();
    }
}
