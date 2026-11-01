using Silk.NET.Windowing;
using Silk.NET.OpenGL;
using SkiaSharp;
using SlimDX;
using SlimDX.Direct3D9;
using Texture = SlimDX.Direct3D9.Texture;
using Color4 = SlimDX.Direct3D9.Color4;
using Color = System.Drawing.Color;

var options=WindowOptions.Default;
options.Size=new Silk.NET.Maths.Vector2D<int>(64,64); options.IsVisible=false;
using var window=Window.Create(options); window.Initialize();
using var gl=window.CreateOpenGL();
using var device=new Device(gl,64,64){GameWidth=64,GameHeight=64};
using var image=new SKBitmap(1,1, SKColorType.Rgba8888, SKAlphaType.Unpremul);
image.SetPixel(0,0,new SKColor(255,255,255,255));
using var white=Texture.FromImage(device,image);
using var target=new Texture(device,64,64,1,Usage.RenderTarget,Format.A8R8G8B8,Pool.Managed);
using var surface=target.GetSurfaceLevel(0);
using var vertices=new VertexBuffer(device,192,Usage.WriteOnly,VertexFormat.None,Pool.Managed);
device.SetRenderTarget(0,surface);
device.SetTexture(0,white);
device.SetRenderState(RenderState.Lighting,true);
device.SetRenderState(RenderState.AlphaBlendEnable,false);
device.SetRenderState(RenderState.LocalViewer,false);
device.EnableLight(0,true);
device.SetTransform(TransformState.View,Matrix.Identity);
device.SetTransform(TransformState.Projection,Matrix.Identity);
int assertions=0;
Color4 primary=new(0.2f,0.6f,0.3f,0.4f), secondary=new(0.7f,0.1f,0.5f,0.8f);
Color4 materialColor=new(0.4f,0.3f,0.2f,0.6f);
Color4 zero=new(0f,0f,0f,0f);
void Fill(float nz,bool packed=false)
{
    var stream=vertices.Lock(0,192,LockFlags.Discard);
    foreach(var point in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(0,1)})
    {
        stream.Write(new Vector3(point.X,point.Y,0.25f));
        stream.Write(new Vector3(0,0,nz)); stream.Write(Vector2.Zero);
        if(packed)
        {
            stream.Write(unchecked((int)0x6633994d));
            stream.Write(unchecked((int)0xccb31a80));
            stream.WriteRange(new float[6]);
        }
        else
        {
            stream.WriteRange(new[]{primary.Red,primary.Green,primary.Blue,primary.Alpha,
                secondary.Red,secondary.Green,secondary.Blue,secondary.Alpha});
        }
    }
    vertices.Unlock();
}
VertexDeclaration Declaration(int mask,bool packed=false)
{
    var fields=new List<VertexElement>{
        new(0,0,DeclarationType.Float3,DeclarationMethod.Default,DeclarationUsage.Position,0),
        new(0,12,DeclarationType.Float3,DeclarationMethod.Default,DeclarationUsage.Normal,0),
        new(0,24,DeclarationType.Float2,DeclarationMethod.Default,DeclarationUsage.TexCoord,0)};
    if((mask&1)!=0) fields.Add(new(0,32,packed?DeclarationType.Color:DeclarationType.Float4,DeclarationMethod.Default,DeclarationUsage.Color,0));
    if((mask&2)!=0) fields.Add(new(0,(short)(packed?36:48),packed?DeclarationType.Color:DeclarationType.Float4,DeclarationMethod.Default,DeclarationUsage.Color,1));
    fields.Add(VertexElement.VertexDeclarationEnd);
    return new VertexDeclaration(device,fields.ToArray());
}
byte[] Draw()
{
    device.Clear(ClearFlags.Target,Color.Black,1,0);
    device.SetStreamSource(0,vertices,0,64);
    device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
    var pixel=new byte[4];
    unsafe { fixed(byte* p=pixel) gl.ReadPixels(32,32,1,1,Silk.NET.OpenGL.PixelFormat.Rgba,PixelType.UnsignedByte,p); }
    return pixel;
}
void Expect(Color4 expected,string name)
{
    var actual=Draw();
    float[] values={expected.Red,expected.Green,expected.Blue,expected.Alpha};
    for(int i=0;i<4;i++) if(Math.Abs(actual[i]-Math.Clamp(values[i],0,1)*255)>3)
        throw new Exception($"{name}: expected={string.Join(',',values)}, actual={string.Join(',',actual)}");
    assertions++;
}
using(var declaration=Declaration(3))
{
    device.VertexDeclaration=declaration;
    device.Material=new Material{Diffuse=materialColor};
    device.SetLight(0,new Light{Type=LightType.Directional,Diffuse=Color.White,Direction=new Vector3(0,0,-1)});
    device.SetTransform(TransformState.World,Matrix.Identity);
    Fill(1);
    Expect(primary,"default ColorVertex=true and DiffuseMaterialSource=Color1");
    device.Material=new Material{Diffuse=new Color4(0f,0f,0f,1f),Specular=materialColor,Power=16};
    device.SetLight(0,new Light{Type=LightType.Directional,Specular=Color.White,Direction=new Vector3(0,0,1)});
    device.SetRenderState(RenderState.SpecularEnable,true);
    Fill(-1);
    var expected=secondary; expected.Alpha=primary.Alpha;
    Expect(expected,"default SpecularMaterialSource=Color2");
    device.SetRenderState(RenderState.SpecularEnable,false);
}
using(var declaration=Declaration(0))
{
    device.VertexDeclaration=declaration;
    device.SetRenderState(RenderState.ColorVertex,false);
    device.Material=new Material{Diffuse=new Color4(1f,0f,0f,1f)};
    device.SetLight(0,new Light{Type=LightType.Directional,Diffuse=Color.White,Direction=new Vector3(0,0,-1)});
    device.SetTransform(TransformState.World,Matrix.Scaling(1,1,2));
    Fill(1);
    Expect(new Color4(0.5f,0f,0f,1f),"default NormalizeNormals=false respects scaled inverse-transpose length");
    device.SetRenderState(RenderState.NormalizeNormals,true);
    Expect(new Color4(1f,0f,0f,1f),"NormalizeNormals=true restores unit normal after scaling");
    device.SetRenderState(RenderState.NormalizeNormals,false);
    Expect(new Color4(0.5f,0f,0f,1f),"NormalizeNormals toggles back off");
    device.SetTransform(TransformState.World,Matrix.Identity);
    Fill(0);
    device.SetRenderState(RenderState.NormalizeNormals,true);
    Expect(new Color4(0f,0f,0f,1f),"zero normal stays finite and receives no directional light");
}
device.SetTransform(TransformState.World,Matrix.Identity);
var components=new[]{RenderState.DiffuseMaterialSource,RenderState.AmbientMaterialSource,RenderState.EmissiveMaterialSource,RenderState.SpecularMaterialSource};
foreach(int mask in new[]{0,1,2,3})
using(var declaration=Declaration(mask))
{
    device.VertexDeclaration=declaration;
    foreach(bool colorVertex in new[]{false,true})
    foreach(var component in components)
    foreach(int source in new[]{0,1,2})
    {
        foreach(var c in components) device.SetRenderState(c,0);
        device.SetRenderState(RenderState.ColorVertex,colorVertex);
        device.SetRenderState(component,source);
        device.SetRenderState(RenderState.Ambient,component==RenderState.AmbientMaterialSource?Color.White.ToArgb():0);
        bool spec=component==RenderState.SpecularMaterialSource;
        device.SetRenderState(RenderState.SpecularEnable,spec);
        var material=new Material{Diffuse=new Color4(0f,0f,0f,1f)};
        if(component==RenderState.DiffuseMaterialSource) material.Diffuse=materialColor;
        if(component==RenderState.AmbientMaterialSource) material.Ambient=materialColor;
        if(component==RenderState.EmissiveMaterialSource) material.Emissive=materialColor;
        if(spec) { material.Specular=materialColor; material.Power=16; }
        device.Material=material;
        device.SetLight(0,new Light{Type=LightType.Directional,
            Diffuse=component==RenderState.DiffuseMaterialSource?Color.White:Color.Black,
            Specular=spec?Color.White:Color.Black,Direction=new Vector3(0,0,spec?1:-1)});
        Fill(spec?-1:1);
        var selected=colorVertex&&source==1&&(mask&1)!=0?primary:
            colorVertex&&source==2&&(mask&2)!=0?secondary:materialColor;
        selected.Alpha=component==RenderState.DiffuseMaterialSource?selected.Alpha:1f;
        Expect(selected,$"{component} source={source} ColorVertex={colorVertex} present={mask}");
    }
}
// Both packed colours must decode BGRA independently; neither can inherit the other's layout.
foreach(bool fvf in new[]{false,true})
{
    using var declaration=Declaration(3,true);
    if(fvf) device.SetFVF(VertexFormat.Position|VertexFormat.Normal|VertexFormat.Texture1|VertexFormat.Diffuse|VertexFormat.Specular);
    else device.VertexDeclaration=declaration;
    if(fvf)
    {
        var stream=vertices.Lock(0,120,LockFlags.Discard);
        foreach(var point in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(0,1)})
        {
            stream.Write(new Vector3(point.X,point.Y,0.25f)); stream.Write(new Vector3(0,0,1));
            stream.Write(unchecked((int)0x6633994d)); stream.Write(unchecked((int)0xccb31a80)); stream.Write(Vector2.Zero);
        }
        vertices.Unlock();
    }
    else Fill(1,true);
    device.SetRenderState(RenderState.ColorVertex,true);
    device.SetRenderState(RenderState.SpecularEnable,false);
    device.SetRenderState(RenderState.Ambient,0);
    device.Material=new Material{Diffuse=materialColor};
    device.SetLight(0,new Light{Type=LightType.Directional,Diffuse=Color.White,Direction=new Vector3(0,0,-1)});
    foreach(int source in new[]{1,2})
    {
        foreach(var c in components) device.SetRenderState(c,0);
        device.SetRenderState(RenderState.DiffuseMaterialSource,source);
        var expected=source==1?new Color4(51/255f,153/255f,77/255f,102/255f):new Color4(179/255f,26/255f,128/255f,204/255f);
        if(fvf)
        {
            device.Clear(ClearFlags.Target,Color.Black,1,0);
            device.SetStreamSource(0,vertices,0,40);
            device.DrawPrimitives(SlimDX.Direct3D9.PrimitiveType.TriangleList,0,1);
            byte[] pixel=new byte[4];
            unsafe { fixed(byte* p=pixel) gl.ReadPixels(32,32,1,1,Silk.NET.OpenGL.PixelFormat.Rgba,PixelType.UnsignedByte,p); }
            float[] channels={expected.Red,expected.Green,expected.Blue,expected.Alpha};
            for(int c=0;c<4;c++) if(Math.Abs(pixel[c]-channels[c]*255)>3) throw new Exception("FVF secondary colour layout mismatch");
            assertions++;
        }
        else Expect(expected,"packed two-colour declaration source="+source);
    }
}
using(var declaration=Declaration(0))
{
    device.VertexDeclaration=declaration;
    device.SetRenderState(RenderState.ColorVertex,false);
    device.SetRenderState(RenderState.SpecularEnable,false);
    device.Material=new Material{Diffuse=new Color4(1f,0f,0f,1f)};
    var normal=new System.Numerics.Vector3(0.3f,0.4f,0.5f);
    var world=Matrix.Scaling(1.5f,0.8f,2f)*Matrix.RotationY(0.25f);
    foreach(float angle in new[]{0f,0.4f})
    foreach(bool normalize in new[]{false,true})
    {
        var view=Matrix.RotationY(angle);
        device.SetTransform(TransformState.World,world); device.SetTransform(TransformState.View,view);
        device.SetRenderState(RenderState.NormalizeNormals,normalize);
        device.SetLight(0,new Light{Type=LightType.Directional,Diffuse=Color.White,Direction=new Vector3(0,0,-1)});
        var stream=vertices.Lock(0,192,LockFlags.Discard);
        foreach(var p in new[]{new Vector2(-1,-1),new Vector2(1,-1),new Vector2(0,1)})
        {
            stream.WriteRange(new[]{p.X,p.Y,0.25f,normal.X,normal.Y,normal.Z,0f,0f,0f,0f,0f,0f,0f,0f,0f,0f});
        }
        vertices.Unlock();
        System.Numerics.Matrix4x4.Invert(world.ToSystemMatrix()*view.ToSystemMatrix(),out var inverse);
        var transformed=System.Numerics.Vector3.TransformNormal(normal,System.Numerics.Matrix4x4.Transpose(inverse));
        if(normalize) transformed=System.Numerics.Vector3.Normalize(transformed);
        var light=System.Numerics.Vector3.Normalize(System.Numerics.Vector3.TransformNormal(System.Numerics.Vector3.UnitZ,view.ToSystemMatrix()));
        float amount=Math.Clamp(System.Numerics.Vector3.Dot(transformed,light),0,1);
        Expect(new Color4(amount,0f,0f,1f),$"CPU inverse-transpose reference camera={angle} normalize={normalize}");
    }
}
if((int)RenderState.NormalizeNormals!=143 || (int)RenderState.LocalViewer!=142 || (int)RenderState.AmbientMaterialSource!=147 || (int)RenderState.EmissiveMaterialSource!=148 || (int)RenderState.SpecularEnable!=29)
    throw new Exception("D3D9 light state numeric values differ from SDK constants");
Console.WriteLine($"PASS: {assertions} lighting state pixel assertions");
device.SetRenderTarget(0,null);
