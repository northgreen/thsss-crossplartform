#nullable enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using static THSSS.Graphics.Metal.MetalNative;

namespace THSSS.Graphics.Metal;
internal sealed unsafe class MetalShader:MetalResource,IRenderShader
{
    internal const int DefaultAttributeBufferIndex = 29;
    private readonly int _vertexInputCount;
    private readonly nint _fragmentLibrary,_vertexFunction,_fragmentFunction;
    private readonly Dictionary<string, (nint Native, bool UsesDefaultAttributes)> _pipelines = new();
    internal MetalShader(MetalRenderBackend owner,ShaderSource source):base(owner,CompileLibrary(owner,source.Vertex,true,source.VertexInputs))
    {
        _vertexInputCount = source.VertexInputs.Length;
        try
        {
            _fragmentLibrary=CompileLibrary(owner,source.Fragment,false,source.VertexInputs);
            _vertexFunction=Require(Call(Native,"newFunctionWithName:",Text("main0")),"vertex entry main0");
            _fragmentFunction=Require(Call(_fragmentLibrary,"newFunctionWithName:",Text("main0")),"fragment entry main0");
        }
        catch{Dispose();throw;}
    }
    internal static string PrepareVertex(string source,string[] inputs)
    {
        for(int i=0;i<inputs.Length;i++)source=Regex.Replace(source,@"(?m)^(\s*)in\s+(\w+)\s+"+Regex.Escape(inputs[i])+@"\s*;","$1layout(location = "+i+") in $2 "+inputs[i]+";");
        return source;
    }
    private static nint CompileLibrary(MetalRenderBackend owner,string glsl,bool vertex,string[] inputs)
    {
        if(vertex)glsl=PrepareVertex(glsl,inputs);
        string msl=TranslateToMsl(glsl,vertex);nint error=0;
        nint lib=Call(owner.Device,"newLibraryWithSource:options:error:",Text(msl),0,(nint)(&error));
        if(lib==0)throw new InvalidOperationException("Metal MSL compilation failed: "+Error(error));return lib;
    }
    internal static string TranslateToMsl(string glsl,bool vertex)=>MetalSourceCompiler.Translate(glsl,vertex);
    internal (nint Native, bool UsesDefaultAttributes) Pipeline(VertexLayout layout,BlendState blend,nint colorFormat,nint depthFormat,bool opaqueColor)
    {
        EnsureAlive();string key=$"{layout.Stride}:{blend}:{colorFormat}:{depthFormat}:{opaqueColor}:"+string.Join(";",layout.Attributes.ToArray());
        if(_pipelines.TryGetValue(key,out var cached))return cached;
        nint d=New("MTLRenderPipelineDescriptor"),v=New("MTLVertexDescriptor");
        try
        {
            Call(d,"setVertexFunction:",_vertexFunction);Call(d,"setFragmentFunction:",_fragmentFunction);
            bool usesVertexStream = false;
            foreach(var a in layout.Attributes.Span)
            {
                if (a.ComponentCount <= 0) continue;
                usesVertexStream = true;
                nint item=Call(Call(v,"attributes"),"objectAtIndexedSubscript:",a.Location);
                Call(item,"setFormat:",AttributeFormat(a));Call(item,"setOffset:",a.ByteOffset);Call(item,"setBufferIndex:",30);
            }
            // Unlike GL, Metal requires a descriptor even for optional shader inputs.
            // Missing attributes read the GL default (0,0,0,1) from a constant stream.
            bool usesDefaultAttributes = false;
            for (int location = 0; location < _vertexInputCount; location++)
            {
                bool supplied = false;
                foreach (var attribute in layout.Attributes.Span)
                    if (attribute.Location == location && attribute.ComponentCount > 0) { supplied = true; break; }
                if (supplied) continue;
                usesDefaultAttributes = true;
                nint item = Call(Call(v, "attributes"), "objectAtIndexedSubscript:", location);
                Call(item, "setFormat:", 31); // MTLVertexFormatFloat4; Metal converts vector lengths.
                Call(item, "setOffset:", 0);
                Call(item, "setBufferIndex:", DefaultAttributeBufferIndex);
            }
            // Metal rejects any configured buffer layout with no referencing attribute.
            if (usesDefaultAttributes)
            {
                nint defaults = Call(Call(v, "layouts"), "objectAtIndexedSubscript:", DefaultAttributeBufferIndex);
                Call(defaults, "setStride:", 4 * sizeof(float));
                Call(defaults, "setStepFunction:", 0); // MTLVertexStepFunctionConstant.
                Call(defaults, "setStepRate:", 0);
            }
            if (usesVertexStream)
            {
                nint binding=Call(Call(v,"layouts"),"objectAtIndexedSubscript:",30);
                Call(binding,"setStride:",layout.Stride);Call(binding,"setStepFunction:",1);
            }
            Call(d,"setVertexDescriptor:",v);
            nint c=Call(Call(d,"colorAttachments"),"objectAtIndexedSubscript:",0);
            Call(c,"setPixelFormat:",colorFormat);Call(c,"setBlendingEnabled:",blend.Enabled?1:0);
            // MTLColorWriteMask RGB = 8|4|2, Alpha = 1. RGB targets discard alpha
            // writes after blending; source alpha still weights the RGB normally.
            Call(c,"setWriteMask:",opaqueColor?14:15);
            foreach(string channel in new[]{"RGB","Alpha"})
            {
                Call(c,"setSource"+channel+"BlendFactor:",Factor(blend.Source));Call(c,"setDestination"+channel+"BlendFactor:",Factor(blend.Destination));
                Call(c,"set"+(channel=="RGB"?"Rgb":"Alpha")+"BlendOperation:",(int)blend.Equation-1);
            }
            Call(d,"setDepthAttachmentPixelFormat:",depthFormat);if(depthFormat==260)Call(d,"setStencilAttachmentPixelFormat:",depthFormat);
            nint error=0;
            nint native = Call(Owner.Device,"newRenderPipelineStateWithDescriptor:error:",d,(nint)(&error));
            if(native==0)throw new InvalidOperationException("Metal pipeline creation failed: "+Error(error));
            var pipeline = (Native: native, UsesDefaultAttributes: usesDefaultAttributes);
            _pipelines.Add(key, pipeline);
            return pipeline;
        }
        finally{Release(d);Release(v);}
    }
    private static nint AttributeFormat(VertexAttribute a)
    {
        if(a.ComponentCount<1||a.ComponentCount>4)throw new ArgumentException("Metal attribute component count must be 1..4.");
        if(a.Type==VertexAttributeType.Float)return 27+a.ComponentCount;
        if(a.ComponentCount==1)return a.Normalized?47:45;
        return(a.Normalized?7:1)+a.ComponentCount-2;
    }
    private static nint Factor(BlendFactor f)=>f switch
    {
        BlendFactor.Zero=>0,BlendFactor.One=>1,BlendFactor.SourceColor=>2,BlendFactor.InverseSourceColor=>3,BlendFactor.SourceAlpha=>4,BlendFactor.InverseSourceAlpha=>5,
        BlendFactor.DestinationColor=>6,BlendFactor.InverseDestinationColor=>7,BlendFactor.DestinationAlpha=>8,BlendFactor.InverseDestinationAlpha=>9,BlendFactor.SourceAlphaSaturate=>10,
        _=>throw new ArgumentOutOfRangeException(nameof(f))
    };
    public int GetUniformLocation(string name)=>-1;
    public void Use()=>EnsureAlive();
    public void SetUniform(int location,float value)=>Legacy(location);
    public void SetUniform(int location,float x,float y)=>Legacy(location);
    public void SetUniform(int location,float x,float y,float z)=>Legacy(location);
    public void SetUniform(int location,float x,float y,float z,float w)=>Legacy(location);
    public void SetUniform(int location,ReadOnlySpan<float> matrix4)=>Legacy(location);
    public void SetUniform(int location,int value)=>Legacy(location);
    private void Legacy(int location){EnsureAlive();if(location>=0)throw new ArgumentException("Metal uniforms use SetUniformBlock.");}
    public override void Dispose()
    {
        if(Native==0)return;foreach(var p in _pipelines.Values)Release(p.Native);_pipelines.Clear();Release(_vertexFunction);Release(_fragmentFunction);Release(_fragmentLibrary);base.Dispose();
    }
}
