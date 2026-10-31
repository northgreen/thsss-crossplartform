// Preserved 2026 native motion raw-operation oracle; no game data or linked library.
#include "../../../native/LegacyMath/software_x87.h"
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <chrono>
#include <cstdlib>
#include <initializer_list>
#include <dlfcn.h>
#if !defined(__linux__) || !defined(__x86_64__)
#error This probe requires Linux x64 (both software and x87 hardware backends).
#endif
using E=software_ext80;
using Motion=decltype(&software_motion_step);
struct Ops { decltype(&software_ext_add) add,sub,mul; decltype(&software_ext_cos) cos,sin; };
struct Api {
 decltype(&software_ext_load_float) load_float;
 decltype(&software_ext_load_double) load_double;
 decltype(&software_ext_store_float) store_float;
 decltype(&software_ext_store_double) store_double;
 decltype(&legacy_math_set_backend) select;
 Motion software,hardware,dispatch;
 Ops sw,hw;
};
static Api api;
template<class T> T symbol(void* library,const char* name){
 void* address=dlsym(library,name);
 if(!address){std::fprintf(stderr,"Missing symbol %s: %s\n",name,dlerror());std::exit(2);}
 return reinterpret_cast<T>(address);
}
void require(int status){if(status){std::fprintf(stderr,"Raw ABI call failed: %d\n",status);std::exit(1);}}
E f(float x){E e;require(api.load_float(x,&e));return e;}
E d(double x){E e;require(api.load_double(x,&e));return e;}
float s(E e){float x;require(api.store_float(&e,&x));return x;}
E n64(E e){double x;require(api.store_double(&e,&x));return d(x);}
E offset(int n){uint64_t m=n<0?-int64_t(n):n; E e{};if(m){int k=0;uint64_t v=m;while(v>>1){v>>=1;++k;} e.significand=m<<(63-k);e.signExp=(16383+k)|(n<0?0x8000:0);}return f(s(e));}
E op(decltype(&software_ext_add) fn,E a,E b,int p){E r;require(fn(&a,&b,p,&r));return r;}
struct Args {float ox1,oy1,vx,sx,ox2,oy2,vy,sy;double dx,dy;int l1,t1,l2,t2,sl,st;};
int call(Motion fn,const Args&a,int p,float*x,float*y){return fn(a.ox1,a.l1,a.oy1,a.t1,a.vx,a.dx,a.sx,a.ox2,a.l2,a.oy2,a.t2,a.vy,a.dy,a.sy,a.sl,a.st,p,x,y);}
void oracle(const Args&a,int p,Ops o,float&rX,float&rY){
 E gx=op(o.add,f(a.ox1),offset(a.l1),p),gy=op(o.add,f(a.oy1),offset(a.t1),p);
 float g1x=s(gx),g1y=s(gy);(void)g1y;
 E px=n64(f(g1x)),vx=n64(f(a.vx)),dir=n64(d(a.dx)),c;
 require(o.cos(&dir,&c));E mx=op(o.mul,vx,c,p);mx=op(o.mul,mx,n64(f(a.sx)),p);
 float x=s(op(o.add,px,mx,p));
 gx=op(o.add,f(a.ox2),offset(a.l2),p);gy=op(o.add,f(a.oy2),offset(a.t2),p);
 float g2x=s(gx),g2y=s(gy);(void)g2x;
 E py=n64(f(g2y)),vy=n64(f(a.vy));dir=n64(d(a.dy));require(o.sin(&dir,&c));
 E my=op(o.mul,vy,c,p);my=op(o.mul,my,n64(f(a.sy)),p);float y=s(op(o.add,py,my,p));
 E u=op(o.sub,f(x),offset(a.sl),p),v=op(o.sub,f(y),offset(a.st),p);
 rX=s(u);rY=s(v);
}
uint64_t rng=0xd219edbeeffeed41ULL;
uint64_t next(){rng^=rng<<13;rng^=rng>>7;rng^=rng<<17;return rng;}
uint32_t bits(float v){uint32_t n;memcpy(&n,&v,4);return n;}
float fl(uint32_t n){float v;memcpy(&v,&n,4);return v;}
double db(uint64_t n){double v;memcpy(&v,&n,8);return v;}
uint32_t fs[]={0,0x80000000,0x7f800000,0xff800000,0x7fc12345,0x7f812345,0xffc54321,0xff854321,1,0x80000001,0x7fffff,0x800000,0x3f800000,0x4b800001,0xcb800001};
uint64_t ds[]={0,0x8000000000000000ULL,0x7ff0000000000000ULL,0xfff0000000000000ULL,0x7ff8123456789abcULL,0x7ff0123456789abcULL,0xfff8123456789abcULL,1,0x8000000000000001ULL,0x43e0000000000000ULL,0x43dfffffffffffffULL,0x7fefffffffffffffULL};
int is[]={0,1,-1,2147483647,(-2147483647-1),16777215,16777216,16777217,16777219,-16777217,-16777219};
float rf(int i){return fl(i%3?uint32_t(next()):fs[next()%(sizeof(fs)/4)]);}
double rd(int i){return db(i%3?next():ds[next()%(sizeof(ds)/8)]);}
int ri(int i){return i%3?int(next()):is[next()%(sizeof(is)/4)];}
Args gen(int i){Args a;float *v[]={&a.ox1,&a.oy1,&a.vx,&a.sx,&a.ox2,&a.oy2,&a.vy,&a.sy};for(auto x:v)*x=rf(i);a.dx=rd(i);a.dy=rd(i);int*t[]={&a.l1,&a.t1,&a.l2,&a.t2,&a.sl,&a.st};for(auto x:t)*x=ri(i);return a;}
bool matches(float x,float y,float ex,float ey){return bits(x)==bits(ex)&&bits(y)==bits(ey);}
void hash_word(uint64_t& hash,uint64_t word,int bytes){
 for(int i=0;i<bytes;++i){hash^=(word>>(i*8))&255;hash*=1099511628211ULL;}
}
void fingerprint(uint64_t& hash,const Args&a){
 for(float v:{a.ox1,a.oy1,a.vx,a.sx,a.ox2,a.oy2,a.vy,a.sy})hash_word(hash,bits(v),4);
 for(double v:{a.dx,a.dy}){uint64_t word;memcpy(&word,&v,8);hash_word(hash,word,8);}
 for(int v:{a.l1,a.t1,a.l2,a.t2,a.sl,a.st})hash_word(hash,uint32_t(v),4);
}
void load(void* library){
#define LOAD(field, name) api.field=symbol<decltype(api.field)>(library,name)
 LOAD(load_float,"software_ext_load_float");LOAD(load_double,"software_ext_load_double");
 LOAD(store_float,"software_ext_store_float");LOAD(store_double,"software_ext_store_double");
 LOAD(select,"legacy_math_set_backend");LOAD(software,"software_motion_step");
 LOAD(hardware,"hardware_motion_step");LOAD(dispatch,"legacy_motion_step");
 LOAD(sw.add,"software_ext_add");LOAD(sw.sub,"software_ext_sub");LOAD(sw.mul,"software_ext_mul");
 LOAD(sw.cos,"software_ext_cos");LOAD(sw.sin,"software_ext_sin");
 LOAD(hw.add,"hardware_ext_add");LOAD(hw.sub,"hardware_ext_sub");LOAD(hw.mul,"hardware_ext_mul");
 LOAD(hw.cos,"hardware_ext_cos");LOAD(hw.sin,"hardware_ext_sin");
#undef LOAD
}
void benchmark(){
 Args a{12,19,2,1,23,29,3,1,0.7,0.8,5,6,7,8,9,10};
 for(int which=0;which<2;++which){uint64_t sum=0;auto t=std::chrono::steady_clock::now();
  for(int i=0;i<500000;++i){float x,y;if(which)require(call(api.hardware,a,24,&x,&y));else oracle(a,24,api.hw,x,y);sum+=bits(x)+uint64_t(bits(y));}
  auto ns=std::chrono::duration_cast<std::chrono::nanoseconds>(std::chrono::steady_clock::now()-t).count();
  printf("BENCH %s %.1f ns checksum=%llu (not performance proof)\n",which?"block":"raw HW",ns/500000.0,(unsigned long long)sum);
 }
}
int main(int argc,char**argv){
 if(argc<2||argc>3||(argc==3&&strcmp(argv[2],"--benchmark"))){fprintf(stderr,"Usage: native_motion_probe library.so [--benchmark]\n");return 2;}
 void* library=dlopen(argv[1],RTLD_NOW|RTLD_LOCAL);
 if(!library){fprintf(stderr,"%s\n",dlerror());return 2;}load(library);
 // Defined normal finite output: both coordinates independently reject one flipped bit.
 Args normal{12,19,2,1,23,29,3,1,0.7,0.8,5,6,7,8,9,10};float nx,ny;
 oracle(normal,24,api.sw,nx,ny);
 if(!matches(nx,ny,nx,ny)||matches(fl(bits(nx)^1),ny,nx,ny)||matches(nx,fl(bits(ny)^1),nx,ny))return 1;
 puts("PASS comparator negative control: normal X/Y one-bit mutations rejected");
 printf("seed=%016llx cases=100000 per backend/PC; stream continues across combinations\n",(unsigned long long)rng);
 for(int backend=1;backend<=2;++backend){require(api.select(backend));auto fn=backend==1?api.software:api.hardware;Ops o=backend==1?api.sw:api.hw;
  for(int p:{24,53}){uint64_t inputs=14695981039346656037ULL,outputs=inputs;
   for(int i=0;i<100000;++i){Args a=gen(i);float ex{},ey{},x{},y{},lx{},ly{};fingerprint(inputs,a);oracle(a,p,o,ex,ey);
    if(call(fn,a,p,&x,&y)||call(api.dispatch,a,p,&lx,&ly)||!matches(x,y,ex,ey)||!matches(lx,ly,ex,ey)){
     printf("FAIL b%d p%d i%d direct=%08x/%08x dispatch=%08x/%08x oracle=%08x/%08x\n",backend,p,i,bits(x),bits(y),bits(lx),bits(ly),bits(ex),bits(ey));return 1;}
    hash_word(outputs,bits(ex),4);hash_word(outputs,bits(ey),4);
    if(i<100)for(auto fptr:{fn,api.dispatch}){float alias;if(call(fptr,a,p,&alias,&alias)||bits(alias)!=bits(ey)){fprintf(stderr,"FAIL alias b%d p%d i%d\n",backend,p,i);return 1;}}
   }
   printf("PASS backend=%d precision=%d 100000 raw-op oracle + direct + dispatch + alias input=%016llx output=%016llx rng=%016llx\n",backend,p,(unsigned long long)inputs,(unsigned long long)outputs,(unsigned long long)rng);
  }
  Args a=gen(1);for(auto fptr:{fn,api.dispatch}){float x=fl(0x12345678),y=fl(0x87654321);
   if(call(fptr,a,17,&x,&y)!=-1||bits(x)!=0x12345678||bits(y)!=0x87654321)return 1;
   if(call(fptr,a,24,nullptr,&y)!=-1||bits(y)!=0x87654321)return 1;
   if(call(fptr,a,53,&x,nullptr)!=-1||bits(x)!=0x12345678)return 1;
  }
 }
 puts("PASS invalid precision/null outputs unchanged");
 if(argc==3)benchmark();
 require(api.select(0));dlclose(library);return 0;
}
