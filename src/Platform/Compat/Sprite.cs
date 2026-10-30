// Cross-platform sprite batching (replaces SlimDX.Direct3D9.Sprite, which used D3DXSprite).
using System;
using System.Collections.Generic;
using System.Drawing;
using Silk.NET.OpenGL;
using THSSS.Graphics;
using SlimDX;

namespace SlimDX.Direct3D9;

/// <summary>
/// Batches 2D quads and draws them with one shader. Coordinates are the game's
/// logical 640x480 space with the origin at the top-left, matching D3D9's
/// pre-transformed vertex layout.
/// </summary>
public class Sprite : IDisposable
{
    private const int FloatsPerVertex = 10;  // x, y, z, w, u, v, r, g, b, a
    private const int VerticesPerQuad = 6;   // two triangles
    private const int MaxBatchSize = 2048;

    private readonly GL _gl;
    private readonly Device _device;
    private readonly float[] _vertices = new float[MaxBatchSize * VerticesPerQuad * FloatsPerVertex];

    private readonly uint _vao;
    private readonly uint _vbo;
    private readonly THSSS.Graphics.IRenderShader _backendShader;
    private readonly THSSS.Graphics.IRenderBuffer _backendBuffer;
    private static readonly THSSS.Graphics.VertexLayout SpriteLayout = new(
        FloatsPerVertex * sizeof(float),
        new[]
        {
            new THSSS.Graphics.VertexAttribute(0, 4, THSSS.Graphics.VertexAttributeType.Float, false, 0),
            new THSSS.Graphics.VertexAttribute(1, 2, THSSS.Graphics.VertexAttributeType.Float, false, 4 * sizeof(float)),
            new THSSS.Graphics.VertexAttribute(2, 4, THSSS.Graphics.VertexAttributeType.Float, false, 6 * sizeof(float)),
        });
    private readonly int _textureLocation;
    private readonly THSSS.Graphics.IRenderBuffer _uniformBlock;
    private readonly byte[] _uniformData = new byte[128];

    private int _batchCount;
    private Texture _currentTextureObject;
    private Matrix _transform = Matrix.Identity;
    private bool _transformIsIdentity = true;
    private bool _disposed;
    private SpriteFlags _flags;
    private (BlendingFactor Source, BlendingFactor Destination, BlendOperation Operation, bool Enabled) _savedBlend;
    private bool _savedDepth, _savedCull;
    private readonly Dictionary<(int, SamplerState), int> _savedSamplers = new();

    public Sprite(Device device)
    {
        _device = device;
        _gl = device.GL;
        _backendShader = device.RenderBackend.CreateShader(Source);
        _textureLocation = _backendShader.GetUniformLocation("uTexture");
        _backendBuffer = device.RenderBackend.CreateBuffer(THSSS.Graphics.BufferKind.Vertex,
            MaxBatchSize * VerticesPerQuad * FloatsPerVertex * sizeof(float));
        // std140 block: two mat4 = 128 bytes; created once and refilled per draw.
        _uniformBlock = device.RenderBackend.CreateBuffer(
            THSSS.Graphics.BufferKind.Uniform, 128);
        if (device.GL != null) (_vao, _vbo) = CreateBuffers();
    }

    public bool Disposed => _disposed;

    /// <summary>
    /// Snapshot the transform at Draw, so changing it does not split a texture batch.
    /// Homogeneous coordinates preserve the shader's clipping and interpolation.
    /// </summary>
    public Matrix Transform
    {
        get => _transform;
        set
        {
            _transform = value;
            _transformIsIdentity = value.ToSystemMatrix().IsIdentity;
        }
    }

    public void Begin() => Begin(SpriteFlags.None);

    public void Begin(SpriteFlags flags)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_device.ActiveSpriteBatch != null)
            throw new InvalidOperationException("End the active sprite batch before beginning another.");
        _batchCount = 0;
        _currentTextureObject = null;
        _flags = flags;
        _savedBlend = _device.CaptureBlendState();
        if ((flags & SpriteFlags.DoNotSaveState) == 0) _device.CaptureSamplerStates(_savedSamplers);
        (_savedDepth, _savedCull) = _device.RenderBackend.QueryRasterState();
        if ((flags & SpriteFlags.DoNotModifyRenderState) != 0)
        {
            _device.ActiveSpriteBatch = this;
            return;
        }
        // Blend factors were just set by the 3D pass (D3D keeps them as device
        // state); honour them here instead of forcing alpha blending, then reset
        // so later sprites use the classic SrcAlpha/1-SrcAlpha.
        _device.SetRenderState(RenderState.AlphaBlendEnable, (flags & SpriteFlags.AlphaBlend) != 0);
        if ((flags & SpriteFlags.AlphaBlend) != 0)
        {
            _device.SetRenderState(RenderState.SourceBlend, Blend.SourceAlpha);
            _device.SetRenderState(RenderState.DestinationBlend, Blend.InverseSourceAlpha);
        }
        _device.ApplyBlendState();
        _device.SetSpriteSamplerDefaults();
        _device.RenderBackend.SetRasterState(false, false);
        _device.ActiveSpriteBatch = this;
    }

    public void End()
    {
        if (_device.ActiveSpriteBatch != this)
            throw new InvalidOperationException("Sprite.End requires this batch to be active.");
        Flush();
        _device.ActiveSpriteBatch = null;
        if ((_flags & SpriteFlags.DoNotSaveState) != 0) return;
        _device.RestoreBlendState(_savedBlend);
        _device.RenderBackend.SetRasterState(_savedDepth, _savedCull);
        _device.RestoreSamplerStates(_savedSamplers);
        _savedSamplers.Clear();
    }

    public void SetTransform(Matrix transform) => Transform = transform;

    public void Draw(Texture texture, Vector3 center, Vector3 position, Color4 color)
        => Draw(texture, null, center, position, color);

    public void Draw(Texture texture, Vector3 center, Vector3 position, Color color)
        => Draw(texture, null, center, position, new Color4(color));

    public void Draw(Texture texture, Rectangle? sourceRect, Vector3 position, Color4 color)
        => Draw(texture, sourceRect, null, position, color);

    public void Draw(Texture texture, Rectangle sourceRect, Vector3 position, Color4 color)
        => Draw(texture, (Rectangle?)sourceRect, null, position, color);

    public void Draw(Texture texture, Rectangle sourceRect, Vector3 rotationCenter, Vector3 position, Color4 color)
        => Draw(texture, (Rectangle?)sourceRect, (Vector3?)rotationCenter, position, color);

    public void Draw(Texture texture, Rectangle? sourceRect, Vector3? rotationCenter, Vector3 position, Color4 color)
    {
        if (texture == null) return;
        if (_device.ActiveSpriteBatch != this)
            throw new InvalidOperationException("Sprite.Draw requires Begin.");
        // Some native backends return a new handle wrapper on each access.
        if (_batchCount > 0 && !ReferenceEquals(_currentTextureObject, texture)) Flush();
        if (_batchCount >= MaxBatchSize) Flush();
        _currentTextureObject = texture;

        var (u0, v0, u1, v1) = AtlasCoordinates(texture, sourceRect);
        // Atlas rows upload in source order, so v=0 is the image's top row. A GL
        // render target keeps its top row at v=1, so only that source inverts.
        if ((texture.Usage & Usage.RenderTarget) != 0 && _device.RenderBackend.Features.OriginBottomLeft)
        {
            (v0, v1) = (1f - v0, 1f - v1);
        }

        float width = sourceRect?.Width ?? texture.Width;
        float height = sourceRect?.Height ?? texture.Height;
        float centerX = rotationCenter?.X ?? 0f;
        float centerY = rotationCenter?.Y ?? 0f;
        float left = position.X - centerX;
        float top = position.Y - centerY;
        float right = left + width;
        float bottom = top + height;

        int o = _batchCount * VerticesPerQuad * FloatsPerVertex;
        // D3D9 sprite vertices are already screen-space and use clockwise winding.
        WriteVertex(ref o, left, top, position.Z, u0, v0, color);
        WriteVertex(ref o, right, top, position.Z, u1, v0, color);
        WriteVertex(ref o, left, bottom, position.Z, u0, v1, color);
        WriteVertex(ref o, right, top, position.Z, u1, v0, color);
        WriteVertex(ref o, right, bottom, position.Z, u1, v1, color);
        WriteVertex(ref o, left, bottom, position.Z, u0, v1, color);
        _batchCount++;
    }

    private void WriteVertex(ref int offset, float x, float y, float z, float u, float v, Color4 color)
    {
        var position = new Vector4(x, y, z, 1f);
        if (!_transformIsIdentity) position = Vector4.Transform(position, _transform);
        _vertices[offset++] = position.X;
        _vertices[offset++] = position.Y;
        _vertices[offset++] = position.Z;
        _vertices[offset++] = position.W;
        _vertices[offset++] = u;
        _vertices[offset++] = v;
        _vertices[offset++] = color.Red;
        _vertices[offset++] = color.Green;
        _vertices[offset++] = color.Blue;
        _vertices[offset++] = color.Alpha;
    }

    private static (float U0, float V0, float U1, float V1) AtlasCoordinates(Texture texture, Rectangle? source)
    {
        if (!source.HasValue) return (0f, 0f, 1f, 1f);
        var r = source.Value;
        return ((float)r.X / texture.Width, (float)r.Y / texture.Height,
                (float)(r.X + r.Width) / texture.Width, (float)(r.Y + r.Height) / texture.Height);
    }

    public void Flush()
    {
        if (_batchCount == 0) return;

        // The projection always covers the fixed logical surface. A back-buffer
        // draw is letterboxed inside the window; an offscreen pass fills its target.
        var target = _device.RenderTarget;
        int width = target?.Width ?? Math.Max(1, _device.FramebufferWidth > 0
            ? _device.FramebufferWidth : _device.Viewport.Width);
        int height = target?.Height ?? Math.Max(1, _device.FramebufferHeight > 0
            ? _device.FramebufferHeight : _device.Viewport.Height);
        var (viewportX, viewportY, viewportWidth, viewportHeight) =
            FitLogicalSurface(width, height);
        _device.RenderBackend.SetViewport(viewportX, viewportY, viewportWidth, viewportHeight, 0f, 1f);

        var projection = Matrix.OrthoOffCenterLH(0, _device.GameWidth, _device.GameHeight, 0, -1, 1);
        var effect = _device.ActiveEffect;
        if (_gl != null) _gl.ActiveTexture(TextureUnit.Texture0);
        _device.RenderBackend.BindTexture(_currentTextureObject?.RenderTexture);
        // Apply the device's current sprite sampler before an FX pass optionally
        // replaces it. Metal/Vulkan retain sampler state across unrelated draws.
        _device.ApplySamplerState();
        if (effect != null)
        {
            effect.SourceTexture = _currentTextureObject;
            effect.Bind(Matrix.Identity, projection);
        }
        else
        {
            _backendShader.Use();
            WriteMatrix(_uniformData.AsSpan(0, 64), Matrix.Identity);
            WriteMatrix(_uniformData.AsSpan(64, 64), projection);
            _device.RenderBackend.SetUniformBlock(0, _uniformBlock, _uniformData);
        }

        _device.ApplyBlendState();
        int floats = _batchCount * VerticesPerQuad * FloatsPerVertex;
        var vertexSpan = _vertices.AsSpan(0, floats);
        var vertexBytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(vertexSpan);
        if (effect != null && _gl == null)
        {
            _backendBuffer.Update(vertexBytes, 0);
            _device.RenderBackend.Draw(effect.BackendShader, _backendBuffer, SpriteLayout,
                THSSS.Graphics.PrimitiveKind.TriangleList, 0, _batchCount * VerticesPerQuad);
        }
        else if (effect != null)
        {
            // Effect passes render with their own program; keep the classic sprite batch.
            _gl.BindVertexArray(_vao);
            _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
            _gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (uint)(floats * sizeof(float)), (ReadOnlySpan<float>)vertexSpan);
            _gl.DrawArrays(Silk.NET.OpenGL.PrimitiveType.Triangles, 0, (uint)(_batchCount * VerticesPerQuad));
            _gl.BindVertexArray(0);
        }
        else
        {
            _backendBuffer.Update(vertexBytes, 0);
            _backendShader.SetUniform(_textureLocation, 0);
            _device.RenderBackend.Draw(_backendShader, _backendBuffer, SpriteLayout,
                THSSS.Graphics.PrimitiveKind.TriangleList, 0, _batchCount * VerticesPerQuad);
        }

        if (_gl != null) _gl.BindVertexArray(0);
        effect?.Unbind();
        _batchCount = 0;
    }

    private (int X, int Y, int Width, int Height) FitLogicalSurface(int width, int height)
    {
        var content = THSSS.Graphics.PresentationGeometry.Fit(width, height, _device.GameWidth, _device.GameHeight);
        return (content.X, content.Y, content.Width, content.Height);
    }

    /// <summary>
    /// std140 layout for SpriteUniforms: uTransform then uProjection, each a 4x4 matrix
    /// stored column-major, so the block is a flat 128-byte buffer.
    /// </summary>
    private static void WriteMatrix(Span<byte> destination, Matrix matrix)
    {
        ReadOnlySpan<float> values = stackalloc float[]
        {
            matrix.M11, matrix.M12, matrix.M13, matrix.M14,
            matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34,
            matrix.M41, matrix.M42, matrix.M43, matrix.M44,
        };
        System.Runtime.InteropServices.MemoryMarshal.AsBytes(values).CopyTo(destination);
    }

    internal static ShaderSource Source => new()
    {
        Vertex = SpriteVertexSource, Fragment = SpriteFragmentSource,
        VertexInputs = new[] { "aPosition", "aTexCoord", "aColor" },
    };

    private const string SpriteVertexSource = @"#version 330 core
layout (location = 0) in vec4 aPosition;
layout (location = 1) in vec2 aTexCoord;
layout (location = 2) in vec4 aColor;
layout (std140) uniform SpriteUniforms {
    mat4 uTransform;
    mat4 uProjection;
};
out vec2 TexCoord;
out vec4 Color;
void main()
{
    gl_Position = uProjection * uTransform * aPosition;
    TexCoord = aTexCoord;
    Color = aColor;
}";

    private const string SpriteFragmentSource = @"#version 330 core
in vec2 TexCoord;
in vec4 Color;
out vec4 FragColor;
uniform sampler2D uTexture;
void main()
{
    FragColor = texture(uTexture, TexCoord) * Color;
}";

    private (uint Vao, uint Vbo) CreateBuffers()
    {
        uint vao = _gl.GenVertexArray();
        uint vbo = _gl.GenBuffer();
        _gl.BindVertexArray(vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (uint)_vertices.Length * sizeof(float),
            (ReadOnlySpan<float>)_vertices, BufferUsageARB.DynamicDraw);

        const uint stride = FloatsPerVertex * sizeof(float);
        _gl.VertexAttribPointer(0, 4, VertexAttribPointerType.Float, false, stride, 0);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, 4 * sizeof(float));
        _gl.EnableVertexAttribArray(1);
        _gl.VertexAttribPointer(2, 4, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        _gl.EnableVertexAttribArray(2);

        _gl.BindVertexArray(0);
        return (vao, vbo);
    }

    public void OnResetDevice() { }

    public void OnLostDevice() { }

    public void Dispose()
    {
        if (_disposed) return;
        if (_device.ActiveSpriteBatch == this) End();
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        _backendShader.Dispose();
        _backendBuffer.Dispose();
        _uniformBlock.Dispose();
        _disposed = true;
    }
}

/// <summary>Color with float components (replaces SlimDX.Color4).</summary>
public struct Color4 : IEquatable<Color4>
{
    public float Red;
    public float Green;
    public float Blue;
    public float Alpha;

    public Color4(float r, float g, float b, float a) { Red = r; Green = g; Blue = b; Alpha = a; }
    public Color4(float r, float g, float b) { Red = r; Green = g; Blue = b; Alpha = 1.0f; }
    public Color4(Color color) { Red = color.R / 255f; Green = color.G / 255f; Blue = color.B / 255f; Alpha = color.A / 255f; }

    public static Color4 White => new(1, 1, 1, 1);
    public static Color4 Black => new(0, 0, 0, 1);
    public static Color4 Transparent => new(0, 0, 0, 0);

    public static implicit operator Color4(Color c) => new(c);

    public static Color4 operator *(Color4 a, Color4 b) =>
        new(a.Red * b.Red, a.Green * b.Green, a.Blue * b.Blue, a.Alpha * b.Alpha);
    public static Color4 operator *(Color4 a, float s) =>
        new(a.Red * s, a.Green * s, a.Blue * s, a.Alpha * s);
    public static Color4 operator +(Color4 a, Color4 b) =>
        new(a.Red + b.Red, a.Green + b.Green, a.Blue + b.Blue, a.Alpha + b.Alpha);

    public bool Equals(Color4 other) => Red == other.Red && Green == other.Green && Blue == other.Blue && Alpha == other.Alpha;
    public override bool Equals(object other) => other is Color4 c && Equals(c);
    public override int GetHashCode() => HashCode.Combine(Red, Green, Blue, Alpha);
    public override string ToString() => $"{{Red:{Red} Green:{Green} Blue:{Blue} Alpha:{Alpha}}}";
}
