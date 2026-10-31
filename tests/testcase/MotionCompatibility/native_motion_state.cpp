// 2026 Linux x64 full dirty caller-state gate, preserved from validated state.cpp.
#include "../../../native/LegacyMath/software_x87.h"
#include <cstdio>
#include <cstring>
#include <cstdint>
#include <cstdlib>
#include <dlfcn.h>
#if !defined(__linux__) || !defined(__x86_64__)
#error This state gate requires Linux x64 and explicit x87 assembly.
#endif
using Motion=decltype(&software_motion_step);
static Motion fn;
static int precision,mode;
static float x,y;
static int status;
__attribute__((noinline)) static void invoke(){status=fn(1,16777217,2,-16777219,3,0.7,1,4,2147483647,5,(-2147483647-1),6,0.8,1,123,-456,precision,mode==1?nullptr:&x,mode==2?nullptr:mode==3?&x:&y);}
template<class T> static T symbol(void* library,const char* name){
 void* address=dlsym(library,name);
 if(!address){std::fprintf(stderr,"Missing symbol %s: %s\n",name,dlerror());std::exit(2);}
 return reinterpret_cast<T>(address);
}
static uint32_t bits(float value){uint32_t word;std::memcpy(&word,&value,4);return word;}
int main(int argc,char** argv){
 if(argc!=2){std::fprintf(stderr,"Usage: native_motion_state library.so\n");return 2;}
 void* library=dlopen(argv[1],RTLD_NOW|RTLD_LOCAL);
 if(!library){std::fprintf(stderr,"%s\n",dlerror());return 2;}
 auto select=symbol<decltype(&legacy_math_set_backend)>(library,"legacy_math_set_backend");
 Motion functions[]={symbol<Motion>(library,"software_motion_step"),symbol<Motion>(library,"hardware_motion_step"),symbol<Motion>(library,"legacy_motion_step")};
 int cases=0;
 for(int backend=1;backend<=2;++backend){
  if(select(backend)){std::fprintf(stderr,"Backend %d unavailable\n",backend);return 2;}
  for(int index=0;index<3;++index)for(int p=0;p<3;++p)for(int m=0;m<4;++m){
   fn=functions[index];precision=p==0?24:p==1?53:17;mode=m;
   x=123;y=456;invoke();const int expected_status=status;
   const uint32_t expected_x=bits(x),expected_y=bits(y);x=123;y=456;
   alignas(16) unsigned char original[108],before[108],after[108];
   uint32_t mxOriginal,mxAfter,mxDirty=0xffe5;uint16_t cw=0x0f7f;
   __asm__ volatile("fnsave %0\n\tfldcw %4\n\tfldz\n\tfldz\n\tfdivp\n\tfld1\n\tfld1\n\tfld1\n\tfld1\n\tfld1\n\tfld1\n\tfld1\n\tfnsave %1\n\tfrstor %1\n\tstmxcsr %2\n\tldmxcsr %3" : "=m"(original),"=m"(before),"=m"(mxOriginal):"m"(mxDirty),"m"(cw):"st","st(1)","st(2)","st(3)","st(4)","st(5)","st(6)","st(7)","memory");
   invoke();
   __asm__ volatile("fnsave %0\n\tfrstor %2\n\tstmxcsr %1\n\tldmxcsr %3":"=m"(after),"=m"(mxAfter):"m"(original),"m"(mxOriginal):"st","st(1)","st(2)","st(3)","st(4)","st(5)","st(6)","st(7)","memory");
   uint16_t seeded_cw,seeded_sw,tags;
   std::memcpy(&seeded_cw,before,2);std::memcpy(&seeded_sw,before+4,2);std::memcpy(&tags,before+8,2);
   bool full=true;for(int reg=0;reg<8;++reg)if(((tags>>(reg*2))&3)==3)full=false;
   if(!full||seeded_cw!=cw||!(seeded_sw&1)||std::memcmp(before,after,108)||mxAfter!=mxDirty){
    std::printf("STATE FAIL backend%d fn%d p%d mode%d full=%d CW=%04x SW=%04x tags=%04x MX=%08x/%08x\n",backend,index,precision,mode,full,seeded_cw,seeded_sw,tags,mxAfter,mxDirty);
    for(int i=0;i<108;++i)if(before[i]!=after[i])std::printf("byte%d %02x/%02x\n",i,before[i],after[i]);
    return 1;
   }
   const bool invalid=precision==17||mode==1||mode==2;
   if(status!=(invalid?-1:0)||status!=expected_status||bits(x)!=expected_x||bits(y)!=expected_y||(invalid&&(bits(x)!=bits(123)||bits(y)!=bits(456)))){
    std::fprintf(stderr,"OUTPUT FAIL backend%d fn%d p%d mode%d status=%d\n",backend,index,precision,mode,status);return 1;
   }
   ++cases;
  }
 }
 if(cases!=72)return 1;
 if(select(0))return 2;
 dlclose(library);
 std::printf("PASS %d dirty full-eight-register 108-byte CW/SW/tags/registers/MXCSR cases incl errors, outputs and alias\n",cases);
 return 0;
}
