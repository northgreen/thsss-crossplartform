using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Silk.NET.OpenGL;
using THSSS.Graphics;

namespace SlimDX.Direct3D9;

public class Effect : IDisposable
{
    private readonly Device _device;
    private readonly EffectKind _kind;
    private readonly Dictionary<string, object> _parameters = new();
    private EffectHandle _technique = "technique1";
    private Effect _previous;
    private bool _begun, _passActive;
    private uint _program, _sampler;
    private uint _previousSampler;
    private bool _bound;
    private IRenderShader _backendShader;
    private IRenderBuffer _backendUniforms;
    private readonly byte[] _uniformData = new byte[EffectShaders.UniformSize];
    private static readonly SamplerStateBlock EffectSampler = new(
        AddressU: (int)TextureAddress.Mirror, AddressV: (int)TextureAddress.Mirror,
        MinFilter: (int)TextureFilter.Linear, MagFilter: (int)TextureFilter.Linear,
        MipFilter: (int)TextureFilter.None, MaxAnisotropy: 1, MipLodBias: 0f,
        BorderRed: 0f, BorderGreen: 0f, BorderBlue: 0f, BorderAlpha: 0f, BorderEnabled: false);

    public Effect(Device device) : this(device, EffectKind.Filter) { }
    public Effect(Device device, byte[] data) : this(device, Decode(data)) { }
    public Effect(Device device, byte[] data, ShaderFlags flags) : this(device, data) { }
    private Effect(Device device, string source) : this(device, EffectSource.Parse(source)) { }
    private Effect(Device device, EffectKind kind)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _kind = kind;
        if (kind == EffectKind.Blur) _parameters.Add("BlurLevel", 0f);
        if (kind == EffectKind.Warp)
        {
            foreach (string name in new[] { "X", "Y", "Time", "Radius" }) _parameters.Add(name, 0f);
            _parameters.Add("ColorKey", 0);
        }
        _parameters.Add("xColoredTexture", null);
    }

    public bool IsDisposed { get; private set; }
    public EffectHandle Technique
    {
        get { CheckDisposed(); return _technique; }
        set { CheckDisposed(); RequireTechnique(value); _technique = value; }
    }
    public void SetTechnique(EffectHandle technique) => Technique = technique;
    public EffectHandle GetTechnique(int index)
    {
        CheckDisposed();
        if (index != 0) throw new ArgumentOutOfRangeException(nameof(index));
        return "technique1";
    }
    public EffectHandle GetTechnique(string name)
    {
        CheckDisposed();
        RequireTechnique(name);
        return name;
    }
    private static void RequireTechnique(EffectHandle handle)
    {
        if (handle.Name != "technique1") throw new ArgumentException("Unknown effect technique: " + handle.Name);
    }
    public int Begin(int flags) => Begin();
    public int Begin()
    {
        CheckDisposed();
        if (_begun) throw new InvalidOperationException("Effect has already begun.");
        _begun = true;
        return 1;
    }
    public void BeginPass(int pass)
    {
        CheckDisposed();
        if (pass != 0) throw new ArgumentOutOfRangeException(nameof(pass));
        if (!_begun || _passActive) throw new InvalidOperationException("BeginPass requires Begin and no active pass.");
        _device.FlushActiveSpriteBatch();
        _previous = _device.ActiveEffect;
        _device.ActiveEffect = this;
        _passActive = true;
    }
    public void CommitChanges()
    {
        CheckDisposed();
        if (!_passActive || _device.ActiveEffect != this) throw new InvalidOperationException("No current effect pass.");
    }
    public void EndPass()
    {
        CheckDisposed();
        if (!_passActive || _device.ActiveEffect != this) throw new InvalidOperationException("Effect passes must end in reverse begin order.");
        _device.FlushActiveSpriteBatch();
        _device.ActiveEffect = _previous;
        _previous = null;
        _passActive = false;
    }
    public void End()
    {
        CheckDisposed();
        if (!_begun || _passActive) throw new InvalidOperationException("End requires Begin and all passes ended.");
        _begun = false;
    }

    public void SetValue<T>(EffectHandle parameter, T value) => SetValue(parameter.Name, value);
    public void SetValue<T>(string name, T value)
    {
        CheckDisposed();
        RequireName(name);
        if (!_parameters.ContainsKey(name)) throw new ArgumentException("Unknown effect parameter: " + name);
        if (_parameters.TryGetValue(name, out var old) && old != null && old.GetType() != typeof(T))
            throw new ArgumentException($"Effect parameter {name} requires {old.GetType().Name}.");
        if (name == "xColoredTexture" && value != null && value is not Texture)
            throw new ArgumentException("xColoredTexture requires a Texture.");
        if (_passActive) _device.FlushActiveSpriteBatch();
        _parameters[name] = value is float[] array ? array.Clone() : value;
    }
    public T GetValue<T>(EffectHandle parameter) => GetValue<T>(parameter.Name);
    public T GetValue<T>(string name)
    {
        CheckDisposed();
        RequireName(name);
        if (!_parameters.TryGetValue(name, out var value)) throw new ArgumentException("Unknown effect parameter: " + name);
        if (value == null) return default;
        if (value is not T typed) throw new InvalidCastException($"Effect parameter {name} is not {typeof(T).Name}.");
        return typed is float[] array ? (T)(object)array.Clone() : typed;
    }
    public void SetTexture(EffectHandle parameter, Texture texture) => SetValue(parameter, texture);
    public void SetTexture(string name, Texture texture) => SetValue(name, texture);
    public void SetMatrix(EffectHandle parameter, Matrix matrix) => SetValue(parameter, matrix);
    public void SetMatrix(string name, Matrix matrix) => SetValue(name, matrix);
    public void SetVector(EffectHandle parameter, Vector4 vector) => SetValue(parameter, vector);
    public void SetFloatArray(EffectHandle parameter, float[] values) => SetValue(parameter, values);
    public void SetFloatArray(string name, float[] values) => SetValue(name, values);
    public void SetInt(EffectHandle parameter, int value) => SetValue(parameter, value);
    public void SetInt(string name, int value) => SetValue(name, value);
    public void SetFloat(EffectHandle parameter, float value) => SetValue(parameter, value);
    public void SetFloat(string name, float value) => SetValue(name, value);
    public void SetBool(EffectHandle parameter, bool value) => SetValue(parameter, value);
    public void SetBool(string name, bool value) => SetValue(name, value);
    public void SetString(EffectHandle parameter, string value) => SetValue(parameter, value);
    public void SetString(string name, string value) => SetValue(name, value);
    public EffectHandle GetParameter(int index)
    {
        CheckDisposed();
        if (index < 0 || index >= _parameters.Count) throw new ArgumentOutOfRangeException(nameof(index));
        return _parameters.Keys.ElementAt(index);
    }
    public EffectHandle GetParameter(string name)
    {
        CheckDisposed();
        RequireName(name);
        if (!_parameters.ContainsKey(name)) throw new ArgumentException("Unknown effect parameter: " + name);
        return name;
    }
    public int GetParameterCount() { CheckDisposed(); return _parameters.Count; }
    public void OnResetDevice() => CheckDisposed();
    public void OnLostDevice() { CheckDisposed(); ReleaseGpu(); }

    internal Texture SourceTexture { get; set; }
    internal IRenderShader BackendShader => _backendShader;

    private void BindBackend(Matrix transform, Matrix projection)
    {
        var backend = _device.RenderBackend;
        _backendShader ??= backend.CreateShader(EffectShaders.BackendSource(_kind));
        _backendUniforms ??= backend.CreateBuffer(THSSS.Graphics.BufferKind.Uniform, EffectShaders.UniformSize);
        WriteMatrix(EffectShaders.TransformOffset, transform.ToOpenGLMatrix());
        WriteMatrix(EffectShaders.ProjectionOffset, projection.ToOpenGLMatrix());
        var source = _parameters["xColoredTexture"] as Texture ?? SourceTexture;
        bool Flipped(Texture texture) => backend.Features.OriginBottomLeft &&
            texture != null && (texture.Usage & Usage.RenderTarget) != 0;
        WriteInt(EffectShaders.FlagsOffset, Flipped(SourceTexture) ? 1 : 0);
        WriteInt(EffectShaders.FlagsOffset + sizeof(int), Flipped(source) ? 1 : 0);
        if (_kind == EffectKind.Warp)
        {
            WriteInt(EffectShaders.FlagsOffset + 2 * sizeof(int), (int)_parameters["ColorKey"]);
            int offset = EffectShaders.WarpOffset;
            foreach (string name in new[] { "X", "Y", "Time", "Radius" })
            {
                WriteFloat(offset, (float)_parameters[name]);
                offset += sizeof(float);
            }
        }
        if (_kind == EffectKind.Blur) WriteFloat(EffectShaders.BlurOffset, (float)_parameters["BlurLevel"]);
        backend.BindTexture(source?.RenderTexture);
        backend.SetSamplerState(EffectSampler);
        backend.SetUniformBlock(0, _backendUniforms, _uniformData);
    }

    private void WriteMatrix(int offset, ReadOnlySpan<float> matrix)
    {
        for (int i = 0; i < matrix.Length; i++) WriteFloat(offset + i * sizeof(float), matrix[i]);
    }
    private void WriteFloat(int offset, float value) => BitConverter.TryWriteBytes(_uniformData.AsSpan(offset, sizeof(float)), value);
    private void WriteInt(int offset, int value) => BitConverter.TryWriteBytes(_uniformData.AsSpan(offset, sizeof(int)), value);
    internal void Bind(Matrix transform, Matrix projection)
    {
        CommitChanges();
        if (_device.GL == null)
        {
            BindBackend(transform, projection);
            return;
        }
        var gl = _device.GL;
        if (_program == 0) _program = EffectShaders.Create(gl, _kind);
        if (_sampler == 0)
        {
            _sampler = gl.GenSampler();
            gl.SamplerParameter(_sampler, SamplerParameterI.TextureWrapS, (int)GLEnum.MirroredRepeat);
            gl.SamplerParameter(_sampler, SamplerParameterI.TextureWrapT, (int)GLEnum.MirroredRepeat);
            gl.SamplerParameter(_sampler, SamplerParameterI.TextureMinFilter, (int)GLEnum.Linear);
            gl.SamplerParameter(_sampler, SamplerParameterI.TextureMagFilter, (int)GLEnum.Linear);
        }
        gl.UseProgram(_program);
        gl.UniformMatrix4(gl.GetUniformLocation(_program, "uTransform"), 1, false, transform.ToOpenGLMatrix());
        gl.UniformMatrix4(gl.GetUniformLocation(_program, "uProjection"), 1, false, projection.ToOpenGLMatrix());
        gl.Uniform1(gl.GetUniformLocation(_program, "xColoredTexture"), 0);
        gl.Uniform1(gl.GetUniformLocation(_program,"uInputFlip"), SourceTexture != null && (SourceTexture.Usage & Usage.RenderTarget) != 0 ? 1 : 0);
        var source = _parameters["xColoredTexture"] as Texture ?? SourceTexture;
        gl.Uniform1(gl.GetUniformLocation(_program,"uSourceFlip"), source != null && (source.Usage & Usage.RenderTarget) != 0 ? 1 : 0);
        foreach (var parameter in _parameters)
        {
            int location = gl.GetUniformLocation(_program, parameter.Key);
            if (parameter.Value is float number) gl.Uniform1(location, number);
            else if (parameter.Value is int integer) gl.Uniform1(location, integer);
        }
        if (_parameters["xColoredTexture"] is Texture texture)
        {
            _device.RenderBackend.BindTexture(texture.RenderTexture);
        }
        if (!_bound)
        {
            _previousSampler = (uint)gl.GetInteger(GLEnum.SamplerBinding);
            _bound = true;
        }
        gl.BindSampler(0, _sampler);
    }
    internal void Unbind()
    {
        if (_device.GL == null) return;
        _device.GL.BindSampler(0, _bound ? _previousSampler : 0);
        _bound = false;
        _device.GL.UseProgram(0);
    }
    private void ReleaseGpu()
    {
        _backendShader?.Dispose();
        _backendUniforms?.Dispose();
        _backendShader = null;
        _backendUniforms = null;
        if (_program != 0) _device.GL.DeleteProgram(_program);
        if (_sampler != 0) _device.GL.DeleteSampler(_sampler);
        _program = _sampler = 0;
    }
    public void Dispose()
    {
        if (IsDisposed) return;
        if (_passActive) EndPass();
        ReleaseGpu();
        _begun = false;
        IsDisposed = true;
    }
    private void CheckDisposed() => ObjectDisposedException.ThrowIf(IsDisposed, this);
    private static void RequireName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Effect parameter name is empty.", nameof(name));
    }
    private static string Decode(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Encoding.UTF8.GetString(data);
    }
    public static Effect FromFile(Device device, string filename) => new(device, File.ReadAllText(filename));
    public static Effect FromFile(Device device, string filename, ShaderFlags flags) => FromFile(device, filename);
    public static Effect FromString(Device device, string source) => new(device, source);
    public static Effect FromMemory(Device device, byte[] data) => new(device, data);
}

public struct EffectHandle
{
    public string Name;
    public static EffectHandle Null = new();
    public static implicit operator EffectHandle(string name) => new() { Name = name };
}

[Flags]
public enum ShaderFlags
{
    None = 0, Debug = 1, SkipValidation = 2, SkipOptimization = 4, PackMatrices = 8
}
