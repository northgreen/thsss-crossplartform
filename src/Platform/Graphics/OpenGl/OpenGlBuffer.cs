using System;
using Silk.NET.OpenGL;
using THSSS.Graphics;

namespace THSSS.Graphics.OpenGl;

internal sealed unsafe class OpenGlBuffer : IRenderBuffer
{
    private readonly GL _gl;
    private readonly BufferTargetARB _target;
    private uint _id;
    private bool _disposed;
    private readonly byte[]? _uniformContents;
    private bool _uniformContentsKnown;

    internal OpenGlBuffer(GL gl, BufferKind kind, int sizeInBytes)
    {
        _gl = gl;
        _target = kind switch
        {
            BufferKind.Index => BufferTargetARB.ElementArrayBuffer,
            BufferKind.Uniform => BufferTargetARB.UniformBuffer,
            _ => BufferTargetARB.ArrayBuffer,
        };
        SizeInBytes = Math.Max(1, sizeInBytes);
        if (kind == BufferKind.Uniform) _uniformContents = new byte[SizeInBytes];
        _id = _gl.GenBuffer();
        _gl.BindBuffer(_target, _id);
        _gl.BufferData(_target, (uint)SizeInBytes, (void*)0, BufferUsageARB.DynamicDraw);
        Handle = new OpenGlHandle(_id);
    }

    internal uint Id => _id;
    internal BufferTargetARB Target => _target;

    public IRenderHandle Handle { get; }
    public int SizeInBytes { get; }

    public void Update(ReadOnlySpan<byte> data, int offsetInBytes)
    {
        if (data.IsEmpty || _id == 0) return;
        _gl.BindBuffer(_target, _id);
        bool fullUniform = _uniformContents != null && offsetInBytes == 0 && data.Length == SizeInBytes;
        // Keep binding side effects; only skip an identical complete uniform upload.
        if (fullUniform && _uniformContentsKnown && data.SequenceEqual(_uniformContents)) return;
        _uniformContentsKnown = false;
        unsafe
        {
            fixed (byte* src = data)
                _gl.BufferSubData(_target, offsetInBytes, (uint)data.Length, src);
        }
        if (fullUniform)
        {
            data.CopyTo(_uniformContents);
            _uniformContentsKnown = true;
        }
    }

    /// <summary>
    /// Attribute pointers are re-established on every draw because a VAO caches the
    /// buffer bound to each attribute, and the game re-uploads across different
    /// buffers between draws.
    /// </summary>
    internal static void BindAttributes(GL gl, OpenGlBuffer buffer, VertexLayout layout)
    {
        gl.BindBuffer(BufferTargetARB.ArrayBuffer, buffer.Id);
        foreach (var attribute in layout.Attributes.Span)
        {
            uint location = (uint)attribute.Location;
            if (attribute.ComponentCount <= 0)
            {
                gl.DisableVertexAttribArray(location);
                continue;
            }
            gl.VertexAttribPointer(location, attribute.ComponentCount,
                attribute.Type == VertexAttributeType.Float
                    ? VertexAttribPointerType.Float
                    : VertexAttribPointerType.UnsignedByte,
                attribute.Normalized, (uint)layout.Stride, (void*)attribute.ByteOffset);
            gl.EnableVertexAttribArray(location);
        }
    }

    internal static void UnbindAttributes(GL gl, VertexLayout layout)
    {
        foreach (var attribute in layout.Attributes.Span)
            gl.DisableVertexAttribArray((uint)attribute.Location);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_id != 0) _gl.DeleteBuffer(_id);
        _id = 0;
    }
}
