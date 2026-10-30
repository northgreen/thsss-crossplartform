#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using static THSSS.Graphics.Metal.MetalNative;

namespace THSSS.Graphics.Metal;

public sealed unsafe partial class MetalRenderBackend
{
    private static readonly bool MeasureFrames = Environment.GetEnvironmentVariable("THSSS_DIAG_FPS") == "1";
    private nint _frameCommand, _activeEncoder, _encoderDepth;
    private MetalFramebuffer? _encoderFramebuffer;
    private readonly HashSet<MetalTexture> _pendingTextures = new();
    private readonly List<MetalBuffer> _vertexUploadPages = new();
    private int _uploadPageIndex, _uploadPageOffset;

    public int FrameSubmissions { get; private set; }
    public int FrameDraws { get; private set; }
    public int FramePasses { get; private set; }
    public double FrameWaitMilliseconds { get; private set; }
    public long FrameUploadBytes { get; private set; }

    private nint CommandBuffer()
    {
        if (!_frameOpen) return Require(Call(_queue, "commandBuffer"), "commandBuffer");
        if (_frameCommand == 0)
            _frameCommand = Require(Call(_queue, "commandBuffer"), "frame commandBuffer");
        return _frameCommand;
    }

    private void EndEncoder()
    {
        if (_activeEncoder == 0) return;
        Call(_activeEncoder, "endEncoding");
        _activeEncoder = 0;
        _encoderFramebuffer = null;
        _encoderDepth = 0;
    }

    private void FinishPass(nint encoder, nint command)
    {
        // Frame commands stay in order on one command buffer; no CPU/GPU wait per draw.
        if (_frameOpen) return;
        Call(encoder, "endEncoding");
        SubmitAndWait(command);
    }

    private void SubmitAndWait(nint command)
    {
        Call(command, "commit");
        TotalSubmissions++;
        if (_frameOpen) FrameSubmissions++;
        long start = MeasureFrames ? Stopwatch.GetTimestamp() : 0;
        Call(command, "waitUntilCompleted");
        if (MeasureFrames && _frameOpen)
            FrameWaitMilliseconds += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (Call(command, "status") == 5)
            throw new InvalidOperationException("Metal command failed: " + Error(Call(command, "error")));
    }

    private void FlushFrameCommands()
    {
        if (_frameCommand == 0) return;
        EndEncoder();
        try { SubmitAndWait(_frameCommand); }
        finally
        {
            _frameCommand = 0;
            _pendingTextures.Clear();
            // Uploaded vertex ranges are reusable only after the command has finished.
            _uploadPageIndex = 0;
            _uploadPageOffset = 0;
        }
    }

    internal void BeforeTextureUpload(MetalTexture texture)
    {
        if (_pendingTextures.Contains(texture)) FlushFrameCommands();
    }

    private (MetalBuffer Buffer, int Offset) SnapshotVertices(MetalBuffer source, VertexLayout layout,
        int startVertex, int vertexCount)
    {
        if (!_frameOpen) return (source, 0);
        int requiredBytes = 0;
        foreach (var attribute in layout.Attributes.Span)
        {
            if (attribute.ComponentCount <= 0) continue;
            int componentBytes = attribute.Type == VertexAttributeType.Float ? sizeof(float) : sizeof(byte);
            long end = attribute.ByteOffset + ((long)startVertex + vertexCount - 1) * layout.Stride
                + (long)attribute.ComponentCount * componentBytes;
            if (attribute.ByteOffset < 0 || end > source.SizeInBytes)
                throw new ArgumentException("Metal vertex attribute exceeds its source buffer.", nameof(layout));
            requiredBytes = Math.Max(requiredBytes, checked((int)end));
        }
        if (requiredBytes == 0) return (source, 0);

        while (true)
        {
            if (_uploadPageIndex == _vertexUploadPages.Count)
                _vertexUploadPages.Add(new MetalBuffer(this, Math.Max(1024 * 1024, requiredBytes)));
            var page = _vertexUploadPages[_uploadPageIndex];
            int offset = checked((_uploadPageOffset + 255) & ~255);
            if (requiredBytes <= page.SizeInBytes - offset)
            {
                var bytes = new ReadOnlySpan<byte>((void*)Call(source.Native, "contents"), requiredBytes);
                page.Update(bytes, offset);
                _uploadPageOffset = offset + requiredBytes;
                FrameUploadBytes += requiredBytes;
                return (page, offset);
            }
            _uploadPageIndex++;
            _uploadPageOffset = 0;
        }
    }
}
