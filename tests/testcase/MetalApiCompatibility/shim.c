#define _POSIX_C_SOURCE 200809L
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

typedef uintptr_t U;
typedef struct { double r,g,b,a; } Color;
typedef struct { double x,y,w,h,near,far; } Viewport;
typedef struct { U x,y,w,h; } Scissor;
typedef struct { double w,h; } Size;
typedef struct { U x,y,z,w,h,d; } Region;
typedef struct Node Node;
struct Node {
    char *type, *text;
    int owned, released, phase;
    U width,height,format,length,load,stride,offset,index;
    U blend[8];
    unsigned char *bytes;
    Node *texture,*color,*depth,*stencil,*attributes,*layouts,*items[32];
    Node *pass,*command,*vertex,*fragment,*pipeline,*view,*layer,*drawable;
    Color clear;
};
static char trace[262144];
static size_t trace_length;
static int live, failures, draws, libraries, fail_command;
static U draw_start,draw_count,draw_kind,vertex_index;
static Node *last_vertex_buffer,*last_pipeline;
static U last_vertex_offset;
static Viewport viewport;
static Scissor scissor;
static double clear_depth;
static float scalar_float;
static Size drawable_size;
static Node *last_drawable, *present_source;
static int presentations;
static char *present_msl;
static void require(int ok, const char *message) {
    if (ok) return;
    fprintf(stderr,"Metal API shim contract failure: %s\n",message);
    abort();
}
static Node *node(const char *type, int owned) {
    Node *n=calloc(1,sizeof(*n)); require(n!=NULL,"allocation");
    n->type=strdup(type); n->owned=owned; n->blend[7]=15; live+=owned; return n;
}
static void event(const char *s) {
    size_t len=strlen(s); require(trace_length+len+2<sizeof(trace),"trace overflow");
    memcpy(trace+trace_length,s,len); trace_length+=len; trace[trace_length++]='\n'; trace[trace_length]=0;
}
Node *shim_window(void) { return node("NSWindow",0); }
int shim_presentations(void) { return presentations; }
U shim_drawable_arg(int i) { require(last_drawable!=NULL,"drawable exists"); return i==0?last_drawable->texture->width:i==1?last_drawable->texture->height:last_drawable->texture->format; }
Node *shim_present_source(void) { return present_source; }
const char *shim_present_msl(void) { return present_msl; }
const char *shim_trace(void) { return trace; }
void shim_clear_trace(void) { trace_length=0; trace[0]=0; }
int shim_live(void) { return live; }
int shim_failures(void) { return failures; }
int shim_draws(void) { return draws; }
int shim_libraries(void) { return libraries; }
U shim_draw_arg(int i) { return i==0?draw_kind:i==1?draw_start:i==2?draw_count:vertex_index; }
U shim_blend_arg(int i) { require(last_pipeline && last_pipeline->color && i>=0 && i<8,"bound pipeline blend descriptor"); return last_pipeline->color->blend[i]; }
const void *shim_vertex_bytes(void) { require(last_vertex_buffer!=NULL,"bound vertex snapshot"); return last_vertex_buffer->bytes+last_vertex_offset; }
double shim_viewport(int i) { const double *p=(const double *)&viewport; return p[i]; }
U shim_scissor(int i) { const U *p=(const U *)&scissor; return p[i]; }
double shim_depth(void) { return clear_depth; }
void shim_fail_next_command(void) { fail_command=1; }
Node *objc_getClass(const char *name) { return node(name,0); }
const char *sel_registerName(const char *name) { return strdup(name); }
Node *MTLCreateSystemDefaultDevice(void) { event("MTLCreateSystemDefaultDevice"); return node("device",1); }

/* Only pointer/integer selectors reach this function. Struct and FP selectors
   are tail-dispatched to separately typed functions below. */
static U integer_message(Node *o,const char *s,U a,U b,U c,U d) {
    (void)d;
    require(o!=NULL && !o->released,"message to null or released object");
    event(s);
    if (!strcmp(s,"alloc")) return (U)node(o->type,1);
    if (!strcmp(s,"init")) return (U)o;
    if (!strcmp(s,"release")) {
        require(o->owned,"release without ownership"); o->released=1; live--; return 0;
    }
    if (!strcmp(s,"stringWithUTF8String:")) { Node *n=node("string",0); n->text=strdup((char *)a); return (U)n; }
    if (!strcmp(s,"UTF8String")) return (U)o->text;
    if (!strcmp(s,"localizedDescription")) { Node *n=node("string",0); n->text=strdup("injected command failure"); return (U)n; }
    if (!strcmp(s,"error")) return (U)node("error",0);
    if (!strcmp(s,"contentView")) { require(!strcmp(o->type,"NSWindow"),"contentView receiver"); if (!o->view) o->view=node("NSView",0); return (U)o->view; }
    if (!strcmp(s,"setWantsLayer:")) { require(!strcmp(o->type,"NSView") && a==1,"layer-backed view"); o->phase=1; return 0; }
    if (!strcmp(s,"setLayer:")) { require(o->phase==1 && !strcmp(((Node *)a)->type,"CAMetalLayer"),"attach CAMetalLayer to view"); o->layer=(Node *)a; return 0; }
    if (!strcmp(s,"setDevice:")) { require(!strcmp(((Node *)a)->type,"device"),"layer device"); return 0; }
    if (!strcmp(s,"setFramebufferOnly:")) { require(a==0,"sampleable drawable setting"); return 0; }
    if (!strcmp(s,"nextDrawable")) {
        require(!strcmp(o->type,"CAMetalLayer") && o->format==80 && o->width && o->height,"configured BGRA layer");
        Node *n=node("drawable",0), *t=node("texture",0); t->width=o->width; t->height=o->height; t->format=80;
        t->bytes=calloc(t->width*t->height,4); n->texture=t; last_drawable=n; return (U)n;
    }
    if (!strcmp(s,"texture")) { require(!strcmp(o->type,"drawable"),"drawable texture receiver"); return (U)o->texture; }
    if (!strcmp(s,"presentDrawable:")) {
        require(!strcmp(o->type,"command") && o->phase==2 && (Node *)a==last_drawable && !o->drawable,"present after encoding before commit, once");
        o->drawable=(Node *)a; presentations++; return 0;
    }
    if (!strcmp(s,"newCommandQueue")) return (U)node("queue",1);
    if (!strcmp(s,"newBufferWithLength:options:")) {
        require(b==0 && a>0,"shared buffer options/length"); Node *n=node("buffer",1);
        n->length=a; n->bytes=calloc(a,1); return (U)n;
    }
    if (!strcmp(s,"contents")) { require(o->bytes!=NULL,"buffer storage"); return (U)o->bytes; }
    if (!strcmp(s,"setWidth:")) { o->width=a; return 0; }
    if (!strcmp(s,"setHeight:")) { o->height=a; return 0; }
    if (!strcmp(s,"setPixelFormat:")) { o->format=a; return 0; }
    if (!strcmp(s,"setStride:")) { o->stride=a; return 0; }
    if (!strcmp(s,"setStepRate:")) return 0;
    if (!strcmp(s,"setOffset:")) { o->offset=a; return 0; }
    if (!strcmp(s,"setBufferIndex:")) { o->index=a; return 0; }
    if (!strcmp(s,"setFormat:")) { o->format=a; return 0; }
    if (!strcmp(s,"setVertexDescriptor:") || !strcmp(s,"setVertexFunction:")) { o->vertex=(Node *)a; return 0; }
    if (!strcmp(s,"setFragmentFunction:")) { o->fragment=(Node *)a; return 0; }
    if (!strcmp(s,"setTexture:")) { o->texture=(Node *)a; return 0; }
    if (!strcmp(s,"setLoadAction:")) { o->load=a; return 0; }
    if (!strcmp(s,"newTextureWithDescriptor:")) {
        Node *desc=(Node *)a; require(desc->width && desc->height,"texture dimensions");
        Node *n=node("texture",1); n->width=desc->width; n->height=desc->height; n->format=desc->format;
        n->bytes=calloc(n->width*n->height,4); return (U)n;
    }
    if (!strcmp(s,"renderPassDescriptor")) return (U)node("pass",0);
    if (!strcmp(s,"colorAttachments") || !strcmp(s,"depthAttachment") || !strcmp(s,"stencilAttachment") || !strcmp(s,"attributes") || !strcmp(s,"layouts")) {
        Node **slot=!strcmp(s,"colorAttachments")?&o->color:!strcmp(s,"depthAttachment")?&o->depth:!strcmp(s,"stencilAttachment")?&o->stencil:!strcmp(s,"attributes")?&o->attributes:&o->layouts;
        if (!*slot) *slot=node(s,0);
        return (U)*slot;
    }
    if (!strcmp(s,"objectAtIndexedSubscript:")) {
        require(a<32,"descriptor array index"); if (!o->items[a]) o->items[a]=node("item",0); return (U)o->items[a];
    }
    if (!strcmp(s,"commandBuffer")) return (U)node("command",0);
    if (!strcmp(s,"renderCommandEncoderWithDescriptor:")) {
        require(o->phase==0 || o->phase==2,"encoder before commit, after prior encoder ends"); Node *n=node("encoder",0); n->command=o; n->pass=(Node *)a; o->phase=1;
        Node *color=n->pass->color?n->pass->color->items[0]:NULL;
        if (color && color->texture && color->load==2) {
            Node *t=color->texture; Color v=color->clear;
            unsigned char p[4]={(unsigned char)lround(v.r*255),(unsigned char)lround(v.g*255),(unsigned char)lround(v.b*255),(unsigned char)lround(v.a*255)};
            if (t->format==80) { unsigned char tmp=p[0]; p[0]=p[2]; p[2]=tmp; }
            for (U i=0;i<t->width*t->height;i++) memcpy(t->bytes+4*i,p,4);
        }
        return (U)n;
    }
    if (!strcmp(s,"endEncoding")) { require(o->command && o->command->phase==1,"end active encoder"); o->command->phase=2; return 0; }
    if (!strcmp(s,"commit")) { require(o->phase==2,"commit after endEncoding"); o->phase=3; return 0; }
    if (!strcmp(s,"waitUntilCompleted")) { require(o->phase==3,"wait after commit"); o->phase=4; return 0; }
    if (!strcmp(s,"status")) { require(o->phase==4,"status after wait"); if (fail_command) { fail_command=0; failures++; return 5; } return 4; }
    if (!strcmp(s,"newLibraryWithSource:options:error:")) {
        Node *source=(Node *)a; require(source->text && strstr(source->text,"metal_stdlib"),"MSL source");
        int present=strstr(source->text,"vertex O pv(")!=NULL;
        require(present || strstr(source->text,"main0"),"translated or presentation MSL entry");
        if (present) { free(present_msl); present_msl=strdup(source->text); }
        require(b==0 && c!=0,"library options/error pointer ABI"); *(U *)c=0; libraries++;
        Node *n=node("library",1); n->phase=present; return (U)n;
    }
    if (!strcmp(s,"newFunctionWithName:")) {
        const char *name=((Node *)a)->text;
        require(o->phase?(!strcmp(name,"pv") || !strcmp(name,"pf")):!strcmp(name,"main0"),"entry name");
        Node *n=node("function",1); n->text=strdup(name); return (U)n;
    }
    if (!strcmp(s,"newRenderPipelineStateWithDescriptor:error:")) {
        require(a && b,"pipeline descriptor/error ABI");
        Node *desc=(Node *)a;
        require(desc->vertex && desc->fragment,"pipeline shader functions");
        if (desc->vertex->text && !strcmp(desc->vertex->text,"pv")) {
            require(desc->fragment->text && !strcmp(desc->fragment->text,"pf"),"presentation fragment entry");
            require(desc->color && desc->color->items[0] && desc->color->items[0]->format==80,"BGRA presentation pipeline");
            Node *n=node("pipeline",1); n->phase=1; n->color=desc->color->items[0]; *(U *)b=0; return (U)n;
        }
        Node *v=desc->vertex;
        require(v->attributes && v->attributes->items[0] && v->layouts && v->layouts->items[30],"vertex descriptor arrays");
        /* Metal rejects nonzero-stride layouts that no attribute references. */
        for (U index=0;index<31;index++) {
            Node *layout=v->layouts->items[index];
            if (!layout || !layout->stride) continue;
            int referenced=0;
            for (U location=0;location<31;location++) {
                Node *item=v->attributes->items[location];
                if (item && item->format && item->index==index) { referenced=1; break; }
            }
            require(referenced,"nonzero-stride vertex buffer layout must have a referencing attribute");
        }
        Node *attribute=v->attributes->items[0], *binding=v->layouts->items[30];
        require(attribute->format==29 && attribute->offset==0 && attribute->index==30 && binding->stride==8,"float2 vertex descriptor format/offset/slot/stride");
        require(desc->color && desc->color->items[0] && desc->color->items[0]->format==70,"RGBA pipeline color format");
        Node *n=node("pipeline",1); n->color=desc->color->items[0]; *(U *)b=0; return (U)n;
    }
    if (!strcmp(s,"newDepthStencilStateWithDescriptor:") || !strcmp(s,"newSamplerStateWithDescriptor:")) { require(a!=0,"state descriptor"); return (U)node("state",1); }
    if (!strcmp(s,"setRenderPipelineState:")) { o->pipeline=(Node *)a; last_pipeline=o->pipeline; return 0; }
    if (!strcmp(s,"setVertexBuffer:offset:atIndex:")) {
        require(a && c==30 && b<((Node *)a)->length,"vertex buffer slot/offset ABI");
        o->vertex=(Node *)a; o->offset=b; vertex_index=c;
        last_vertex_buffer=o->vertex; last_vertex_offset=b; return 0;
    }
    if (!strcmp(s,"setVertexBytes:length:atIndex:") || !strcmp(s,"setFragmentBytes:length:atIndex:")) { require(a && b>0 && c<30,"uniform bytes ABI"); return 0; }
    if (!strcmp(s,"setFragmentTexture:atIndex:")) {
        require(a && b==(o->pipeline && o->pipeline->phase?0:1),"fragment texture binding slot");
        o->texture=(Node *)a; return 0;
    }
    if (!strcmp(s,"drawPrimitives:vertexStart:vertexCount:")) {
        if (o->pipeline && o->pipeline->phase) {
            require(o->command && o->command->phase==1 && o->texture && a==3 && b==0 && c==3,"presentation generated triangle");
            require(o->pass->color->items[0]->texture==last_drawable->texture,"presentation targets drawable");
            present_source=o->texture; draws++; return 0;
        }
        require(o->command && o->command->phase==1 && o->pipeline && o->vertex,"draw requires active encoder/pipeline/vertices");
        require(a==3 && c==3 && o->offset+(b+c)*8<=o->vertex->length,"fixture triangle draw arguments");
        draws++; draw_kind=a; draw_start=b; draw_count=c; return 0;
    }
    static const char *blend_setters[]={"setBlendingEnabled:","setSourceRGBBlendFactor:","setDestinationRGBBlendFactor:","setSourceAlphaBlendFactor:","setDestinationAlphaBlendFactor:","setRgbBlendOperation:","setAlphaBlendOperation:","setWriteMask:"};
    for (U i=0;i<sizeof(blend_setters)/sizeof(*blend_setters);i++) if (!strcmp(s,blend_setters[i])) { o->blend[i]=a; return 0; }
    static const char *setters[]={"setTextureType:","setUsage:","setStorageMode:","setStoreAction:","setClearStencil:","setFormat:","setStepFunction:","setVertexFunction:","setFragmentFunction:","setVertexDescriptor:","setDepthAttachmentPixelFormat:","setStencilAttachmentPixelFormat:","setCullMode:","setFrontFacingWinding:","setDepthCompareFunction:","setDepthWriteEnabled:","setDepthStencilState:","setFragmentTexture:atIndex:","setFragmentSamplerState:atIndex:","setSAddressMode:","setTAddressMode:","setMinFilter:","setMagFilter:","setMipFilter:","setMaxAnisotropy:"};
    for (size_t i=0;i<sizeof(setters)/sizeof(*setters);i++) if (!strcmp(s,setters[i])) return 0;
    fprintf(stderr,"unsupported selector: %s (%s)\n",s,o->type); abort();
}
static void color_message(Node *o,const char *s,Color v) { event(s); o->clear=v; }
static void viewport_message(Node *o,const char *s,Viewport v) { require(o->command!=NULL,"viewport encoder"); event(s); viewport=v; }
static void scissor_message(Node *o,const char *s,Scissor v) { require(o->command!=NULL,"scissor encoder"); event(s); scissor=v; }
static void double_message(Node *o,const char *s,double v) { (void)o; event(s); clear_depth=v; }
static void float_message(Node *o,const char *s,float v) { (void)o; event(s); scalar_float=v; }
static void size_message(Node *o,const char *s,Size v) { require(!strcmp(o->type,"CAMetalLayer") && v.w>0 && v.h>0,"drawable size ABI"); event(s); drawable_size=v; o->width=(U)v.w; o->height=(U)v.h; }
static void check_region(Node *o,Region r,U level,U pitch) {
    require(level==0 && r.z==0 && r.d==1 && r.w && r.h && r.x+r.w<=o->width && r.y+r.h<=o->height && pitch>=r.w*4,"region/level/row pitch ABI");
}
static void upload_message(Node *o,const char *s,Region r,U level,const void *bytes,U pitch) {
    event(s); check_region(o,r,level,pitch); require(bytes!=NULL,"upload pointer");
    for (U y=0;y<r.h;y++) memcpy(o->bytes+((r.y+y)*o->width+r.x)*4,(const char *)bytes+y*pitch,r.w*4);
}
static void read_message(Node *o,const char *s,void *bytes,U pitch,Region r,U level) {
    event(s); check_region(o,r,level,pitch); require(bytes!=NULL,"read pointer");
    for (U y=0;y<r.h;y++) memcpy((char *)bytes+y*pitch,o->bytes+((r.y+y)*o->width+r.x)*4,r.w*4);
}
void *shim_dispatch(const char *s) {
    if (!strcmp(s,"setClearColor:")) return color_message;
    if (!strcmp(s,"setViewport:")) return viewport_message;
    if (!strcmp(s,"setScissorRect:")) return scissor_message;
    if (!strcmp(s,"setClearDepth:")) return double_message;
    if (!strcmp(s,"setLodMinClamp:")) return float_message;
    if (!strcmp(s,"setDrawableSize:")) return size_message;
    if (!strcmp(s,"replaceRegion:mipmapLevel:withBytes:bytesPerRow:")) return upload_message;
    if (!strcmp(s,"getBytes:bytesPerRow:fromRegion:mipmapLevel:")) return read_message;
    return integer_message;
}
