using System.Reflection;
using Silk.NET.OpenGL;
using THSSS.Graphics;
using BufferKind = THSSS.Graphics.BufferKind;

static class UniformBufferReadback
{
    public static unsafe void Run(GL gl, IRenderBackend backend)
    {
        int oldUniform = gl.GetInteger(GLEnum.UniformBufferBinding);
        int oldVertex = gl.GetInteger(GLEnum.ArrayBufferBinding);
        int oldIndexed;
        gl.GetInteger(GLEnum.UniformBufferBinding, 0, &oldIndexed);
        using var first = backend.CreateBuffer(BufferKind.Uniform, 16);
        using var second = backend.CreateBuffer(BufferKind.Uniform, 16);
        using var vertex = backend.CreateBuffer(BufferKind.Vertex, 16);
        byte[] a = Enumerable.Repeat((byte)0x35, 16).ToArray();
        byte[] b = Enumerable.Repeat((byte)0xCA, 16).ToArray();
        try
        {
            first.Update(a, 0);
            int firstId = gl.GetInteger(GLEnum.UniformBufferBinding);
            Check(gl, first, BufferTargetARB.UniformBuffer, a, true, "full A");
            first.Update(a, 0);
            Check(gl, first, BufferTargetARB.UniformBuffer, a, true, "repeat A");
            first.Update(b, 0);
            Check(gl, first, BufferTargetARB.UniformBuffer, b, true, "full B");
            first.Update(b, 0);
            Check(gl, first, BufferTargetARB.UniformBuffer, b, true, "repeat B");
            int boundBeforeEmpty = gl.GetInteger(GLEnum.UniformBufferBinding);
            first.Update(ReadOnlySpan<byte>.Empty, 0);
            if (gl.GetInteger(GLEnum.UniformBufferBinding) != boundBeforeEmpty)
                throw new Exception("Empty uniform update changed the binding");
            Check(gl, first, BufferTargetARB.UniformBuffer, b, true, "empty preserves contents");
            first.Update(a.AsSpan(0, 4), 4);
            byte[] partial = (byte[])b.Clone();
            a.AsSpan(0, 4).CopyTo(partial.AsSpan(4));
            Check(gl, first, BufferTargetARB.UniformBuffer, partial, false, "partial invalidation");
            first.Update(b, 0);
            Check(gl, first, BufferTargetARB.UniformBuffer, b, true, "full B repairs partial");
            second.Update(b, 0);
            int secondId = gl.GetInteger(GLEnum.UniformBufferBinding);
            if (secondId == firstId) throw new Exception("Uniform buffers share a GPU object");
            Check(gl, second, BufferTargetARB.UniformBuffer, b, true, "independent same-content buffer");
            first.Update(b, 0);
            if (gl.GetInteger(GLEnum.UniformBufferBinding) != firstId)
                throw new Exception("Repeated uniform update did not restore binding");
            Check(gl, first, BufferTargetARB.UniformBuffer, b, true, "repeat after buffer switch");
            backend.SetUniformBlock(0, second, b);
            backend.SetUniformBlock(0, first, b);
            int indexed;
            gl.GetInteger(GLEnum.UniformBufferBinding, 0, &indexed);
            if (indexed != firstId) throw new Exception("Repeated SetUniformBlock did not restore indexed binding");
            Check(gl, first, BufferTargetARB.UniformBuffer, b, true, "indexed binding restoration");
            vertex.Update(b, 0);
            fixed (byte* p = a) gl.BufferSubData(BufferTargetARB.ArrayBuffer, 0, (nuint)a.Length, p);
            Check(gl, vertex, BufferTargetARB.ArrayBuffer, a, false, "raw vertex mutation");
            vertex.Update(b, 0);
            Check(gl, vertex, BufferTargetARB.ArrayBuffer, b, false, "vertex repeat must upload");
            using var disposed = backend.CreateBuffer(BufferKind.Uniform, 16);
            disposed.Update(a, 0);
            disposed.Dispose();
            second.Update(b, 0);
            int boundBeforeDisposed = gl.GetInteger(GLEnum.UniformBufferBinding);
            disposed.Update(a, 0);
            if (gl.GetInteger(GLEnum.UniformBufferBinding) != boundBeforeDisposed)
                throw new Exception("Disposed uniform update changed the binding");
            if (gl.GetError() != GLEnum.NoError) throw new Exception("Uniform readback generated a GL error");
        }
        finally
        {
            gl.BindBufferBase(BufferTargetARB.UniformBuffer, 0, (uint)oldIndexed);
            gl.BindBuffer(BufferTargetARB.UniformBuffer, (uint)oldUniform);
            gl.BindBuffer(BufferTargetARB.ArrayBuffer, (uint)oldVertex);
        }
        Console.WriteLine("PASS: uniform full/repeat/change/partial GPU storage, independent buffers, generic/indexed bindings, vertex reupload");
    }

    private static unsafe void Check(GL gl, IRenderBuffer buffer, BufferTargetARB target,
        byte[] expected, bool contentsKnown, string step)
    {
        byte[] actual = new byte[expected.Length];
        fixed (byte* p = actual) gl.GetBufferSubData(target, 0, (nuint)actual.Length, p);
        if (!actual.AsSpan().SequenceEqual(expected))
            throw new Exception($"{step}: GPU storage mismatch: {Convert.ToHexString(actual)}");
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var type = buffer.GetType();
        bool known = (bool)(type.GetField("_uniformContentsKnown", flags)?.GetValue(buffer)
            ?? throw new Exception("Uniform cache validity field missing"));
        var cached = type.GetField("_uniformContents", flags)?.GetValue(buffer) as byte[];
        if (known != contentsKnown || (contentsKnown && (cached == null || !cached.AsSpan().SequenceEqual(expected))))
            throw new Exception($"{step}: uniform cache state disagrees with GPU storage");
        if (target == BufferTargetARB.ArrayBuffer && cached != null)
            throw new Exception("Vertex buffer must not use the uniform contents cache");
    }
}
