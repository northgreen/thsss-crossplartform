using System;
using Silk.NET.OpenGL;
using THSSS.Graphics;

namespace SlimDX.Direct3D9;

internal static class EffectShaders
{
    internal const string Vertex = @"#version 330 core
layout(location=0) in vec4 aPosition;
layout(location=1) in vec2 aTexCoord;
layout(location=2) in vec4 aColor;
uniform mat4 uTransform;
uniform mat4 uProjection;
out vec2 TexCoord;
void main() {
    gl_Position = uProjection * uTransform * aPosition;
    TexCoord = aTexCoord;
}";
    private const string Header = @"#version 330 core
in vec2 TexCoord;
out vec4 FragColor;
uniform sampler2D xColoredTexture;
uniform bool uSourceFlip;
uniform bool uInputFlip;
vec2 logicalUV() { return uInputFlip ? vec2(TexCoord.x,1.0-TexCoord.y) : TexCoord; }
vec4 sampleSource(vec2 uv) { return texture(xColoredTexture, uSourceFlip ? vec2(uv.x,1.0-uv.y) : uv); }
";
    private const string Blur = @"
uniform float BlurLevel;
void main() {
    vec2 tex = logicalUV();
    vec4 color = sampleSource(tex);
    color += sampleSource(tex + BlurLevel);
    color += sampleSource(tex - BlurLevel);
    color /= 3.0;
    color.rgb -= vec3(BlurLevel * 50.0);
    FragColor = color;
}";
    private const string Warp = @"
uniform float X, Y, Time, Radius;
uniform int ColorKey;
vec2 displacement(vec2 tex, vec2 center, float strength) {
    vec2 delta = tex - center;
    delta.y *= 3.0 / 4.0;
    float dist2 = dot(delta, delta);
    if (dist2 >= Radius * Radius) return vec2(0.0);
    float k = strength * (acos(sqrt(dist2) / Radius - 1.0) - acos(0.0));
    return vec2(delta.x * k, delta.y * k / 3.0 * 4.0);
}
void main() {
    vec2 center1 = vec2(X + 20.0*cos(Time)/640.0, Y + 30.0*sin(1.5*Time)/640.0);
    vec2 center2 = vec2(X + 20.0*cos(1.5*Time)/640.0, Y + 30.0*sin(1.0*Time)/640.0);
    vec2 tex = logicalUV();
    vec2 dest = tex;
    dest -= displacement(tex, center1, 0.3);
    dest -= displacement(tex, center2, 0.3);
    dest -= displacement(tex, vec2(X,Y), 0.25);
    vec4 color = sampleSource(dest);
    vec2 delta = tex - vec2(X,Y);
    delta.y *= 3.0 / 4.0;
    float dist2 = dot(delta, delta);
    float r = Radius + 0.02;
    if (dist2 < r*r) {
        float amount = 10.0*(r*r-dist2);
        if (ColorKey == 0 || ColorKey == 3 || ColorKey == 4) color.r -= amount;
        if (ColorKey == 1 || ColorKey == 3 || ColorKey == 5) color.g -= amount;
        if (ColorKey == 2 || ColorKey == 4 || ColorKey == 5) color.b -= amount;
    }
    FragColor = color;
}";

    private const int VectorSize = 4 * sizeof(float);
    private const int MatrixSize = 4 * VectorSize;
    internal const int TransformOffset = 0;
    internal const int ProjectionOffset = TransformOffset + MatrixSize;
    internal const int FlagsOffset = ProjectionOffset + MatrixSize;
    internal const int WarpOffset = FlagsOffset + VectorSize;
    internal const int BlurOffset = WarpOffset + VectorSize;
    internal const int UniformSize = BlurOffset + VectorSize;

    internal static string Fragment(EffectKind kind) => Header + (kind switch
    {
        EffectKind.Blur => Blur,
        EffectKind.Warp => Warp,
        _ => "void main() { FragColor = sampleSource(logicalUV()); }"
    });

    internal static ShaderSource BackendSource(EffectKind kind) => new()
    {
        Vertex = WithUniformBlock(Vertex),
        Fragment = WithUniformBlock(Fragment(kind)),
        VertexInputs = new[] { "aPosition", "aTexCoord", "aColor" },
    };

    private static string WithUniformBlock(string source)
    {
        string block = $@"layout (std140) uniform EffectUniforms {{
    layout(offset = {TransformOffset}) mat4 effectTransform;
    layout(offset = {ProjectionOffset}) mat4 effectProjection;
    layout(offset = {FlagsOffset}) ivec4 effectFlags;
    layout(offset = {WarpOffset}) vec4 effectWarp;
    layout(offset = {BlurOffset}) vec4 effectBlur;
}};
#define uTransform effectTransform
#define uProjection effectProjection
#define uInputFlip (effectFlags.x != 0)
#define uSourceFlip (effectFlags.y != 0)
#define ColorKey effectFlags.z
#define X effectWarp.x
#define Y effectWarp.y
#define Time effectWarp.z
#define Radius effectWarp.w
#define BlurLevel effectBlur.x
";
        foreach (string declaration in new[] {
            "uniform mat4 uTransform;", "uniform mat4 uProjection;",
            "uniform bool uSourceFlip;", "uniform bool uInputFlip;",
            "uniform float BlurLevel;", "uniform float X, Y, Time, Radius;",
            "uniform int ColorKey;" })
            source = source.Replace(declaration, string.Empty, StringComparison.Ordinal);
        return source.Replace("#version 330 core", "#version 330 core\n" + block, StringComparison.Ordinal);
    }

    internal static uint Create(GL gl, EffectKind kind)
    {
        uint vertex = Compile(gl, ShaderType.VertexShader, Vertex);
        uint fragment = 0, program = 0;
        try
        {
            fragment = Compile(gl, ShaderType.FragmentShader, Fragment(kind));
            program = gl.CreateProgram();
            gl.AttachShader(program, vertex);
            gl.AttachShader(program, fragment);
            gl.LinkProgram(program);
            gl.GetProgram(program, ProgramPropertyARB.LinkStatus, out int linked);
            if (linked == 0) throw new InvalidOperationException($"Effect {kind} link failed: {gl.GetProgramInfoLog(program)}");
            return program;
        }
        catch
        {
            if (program != 0) gl.DeleteProgram(program);
            throw;
        }
        finally
        {
            gl.DeleteShader(vertex);
            if (fragment != 0) gl.DeleteShader(fragment);
        }
    }

    private static uint Compile(GL gl, ShaderType type, string source)
    {
        uint shader = gl.CreateShader(type);
        gl.ShaderSource(shader, source);
        gl.CompileShader(shader);
        gl.GetShader(shader, ShaderParameterName.CompileStatus, out int compiled);
        if (compiled != 0) return shader;
        string error = gl.GetShaderInfoLog(shader);
        gl.DeleteShader(shader);
        throw new InvalidOperationException($"Effect {type} compilation failed: {error}");
    }
}
