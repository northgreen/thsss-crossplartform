using System;
using System.Collections.Generic;
using Silk.NET.OpenGL;

namespace SlimDX.Direct3D9;

public partial class Device
{
    private readonly Dictionary<(int, SamplerState), int> _samplerStates = new();
    private uint _sampler0;

    internal void CaptureSamplerStates(Dictionary<(int, SamplerState), int> states)
    {
        states.Clear();
        foreach (var state in _samplerStates) states.Add(state.Key, state.Value);
    }

    internal void RestoreSamplerStates(Dictionary<(int, SamplerState), int> states)
    {
        _samplerStates.Clear();
        foreach (var state in states) _samplerStates.Add(state.Key, state.Value);
        ApplySamplerState();
    }

    internal void SetSpriteSamplerDefaults()
    {
        // D3DXSprite.Begin establishes its own sampler instead of inheriting the
        // D3D device's point-filter defaults from the preceding 3D draw.
        StoreSamplerState(0, SamplerState.AddressU, (int)TextureAddress.Clamp);
        StoreSamplerState(0, SamplerState.AddressV, (int)TextureAddress.Clamp);
        StoreSamplerState(0, SamplerState.MinFilter, (int)TextureFilter.Linear);
        StoreSamplerState(0, SamplerState.MagFilter, (int)TextureFilter.Linear);
        StoreSamplerState(0, SamplerState.MipFilter, (int)TextureFilter.Linear);
        StoreSamplerState(0, SamplerState.MaxMipLevel, 0);
        StoreSamplerState(0, SamplerState.MaxAnisotropy, 1);
        StoreSamplerState(0, SamplerState.MipMapLodBias, 0);
    }

    private void StoreSamplerState(int sampler, SamplerState state, int value)
    {
        if (sampler < 0 || sampler > 15) throw new ArgumentOutOfRangeException(nameof(sampler));
        FlushActiveSpriteBatch();
        _samplerStates[(sampler,state)] = value;
    }
    internal void ApplySamplerState()
    {
        int State(SamplerState state,int fallback) => _samplerStates.TryGetValue((0,state),out int value) ? value : fallback;
        if (GL == null)
        {
            int border = State(SamplerState.BorderColor,0);
            RenderBackend.SetSamplerState(new THSSS.Graphics.SamplerStateBlock(
                State(SamplerState.AddressU,1), State(SamplerState.AddressV,1),
                State(SamplerState.MinFilter,1), State(SamplerState.MagFilter,1),
                State(SamplerState.MipFilter,0), State(SamplerState.MaxAnisotropy,1),
                BitConverter.Int32BitsToSingle(State(SamplerState.MipMapLodBias,0)),
                ((border>>16)&255)/255f, ((border>>8)&255)/255f, (border&255)/255f, ((border>>24)&255)/255f, false));
            return;
        }
        if (_sampler0 == 0) _sampler0 = GL.GenSampler();
        int Address(int value) => value switch
        {
            (int)TextureAddress.Mirror => (int)GLEnum.MirroredRepeat,
            (int)TextureAddress.Clamp => (int)GLEnum.ClampToEdge,
            (int)TextureAddress.Border => (int)GLEnum.ClampToBorder,
            _ => (int)GLEnum.Repeat,
        };
        GL.SamplerParameter(_sampler0,SamplerParameterI.TextureWrapS,Address(State(SamplerState.AddressU,(int)TextureAddress.Wrap)));
        GL.SamplerParameter(_sampler0,SamplerParameterI.TextureWrapT,Address(State(SamplerState.AddressV,(int)TextureAddress.Wrap)));
        bool point = State(SamplerState.MinFilter,(int)TextureFilter.Point) == (int)TextureFilter.Point;
        int mip = State(SamplerState.MipFilter,(int)TextureFilter.None);
        var minFilter = mip switch
        {
            (int)TextureFilter.Point => point ? GLEnum.NearestMipmapNearest : GLEnum.LinearMipmapNearest,
            (int)TextureFilter.Linear => point ? GLEnum.NearestMipmapLinear : GLEnum.LinearMipmapLinear,
            _ => point ? GLEnum.Nearest : GLEnum.Linear,
        };
        GL.SamplerParameter(_sampler0,SamplerParameterI.TextureMinFilter,(int)minFilter);
        GL.SamplerParameter(_sampler0,SamplerParameterF.TextureMinLod,(float)State(SamplerState.MaxMipLevel,0));
        GL.SamplerParameter(_sampler0,SamplerParameterF.TextureLodBias,BitConverter.Int32BitsToSingle(State(SamplerState.MipMapLodBias,0)));
        GL.SamplerParameter(_sampler0,SamplerParameterI.TextureMagFilter,
            State(SamplerState.MagFilter,(int)TextureFilter.Point) == (int)TextureFilter.Point ? (int)GLEnum.Nearest : (int)GLEnum.Linear);
        int packed = State(SamplerState.BorderColor,0);
        GL.SamplerParameter(_sampler0,SamplerParameterF.TextureBorderColor,new float[]{
            ((packed>>16)&255)/255f,((packed>>8)&255)/255f,(packed&255)/255f,((packed>>24)&255)/255f});
        GL.BindSampler(0,_sampler0);
    }
}
