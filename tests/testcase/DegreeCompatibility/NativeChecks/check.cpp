// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#include "common.h"
#include <initializer_list>
using Kernel=int(*)(double,float,int,double*);
#if defined(__x86_64__)
#include "raw_oracle.h"
#endif
static uint64_t rng=0xa762192bf9134e18ULL;
static uint64_t next(){rng^=rng<<13;rng^=rng>>7;rng^=rng<<17;return rng;}
static unsigned state_cases;
static void state(Kernel fn,int precision,bool null_output,uint64_t rad,uint32_t delta){
 double out=cast<double>(UINT64_C(0x123456789abcdef0));
#if defined(__x86_64__)
 alignas(16) unsigned char original[108],before[108],after[108];
 uint32_t original_mxcsr,mxcsr=0xffe5,after_mxcsr;uint16_t cw=0x0f7f;
 __asm__ volatile(
  "fnsave %[original]; stmxcsr %[original_mxcsr]; fninit; fldcw %[cw];"
  "fldz; fldz; fdivp; fstp %%st(0);"
  "fld1; fldl2t; fldl2e; fldpi; fldlg2; fldln2; fldz; fld1; fchs;"
  "fnsave %[before]; frstor %[before]; ldmxcsr %[mxcsr]"
  :[original]"=m"(original),[original_mxcsr]"=m"(original_mxcsr),[before]"=m"(before)
  :[cw]"m"(cw),[mxcsr]"m"(mxcsr)
  :"memory","st","st(1)","st(2)","st(3)","st(4)","st(5)","st(6)","st(7)");
#else
 uint64_t original_control,original_status,before_control=0x01c00000,before_status=0x9f,after_control,after_status;
 __asm__ volatile("mrs %0, fpcr; mrs %1, fpsr; msr fpcr, %2; msr fpsr, %3"
  :"=&r"(original_control),"=&r"(original_status):"r"(before_control),"r"(before_status):"memory");
#endif
 int status=fn(cast<double>(rad),cast<float>(delta),precision,null_output?nullptr:&out);
#if defined(__x86_64__)
 __asm__ volatile(
  "fnsave %[after]; stmxcsr %[after_mxcsr]; frstor %[original]; ldmxcsr %[original_mxcsr]"
  :[after]"=m"(after),[after_mxcsr]"=m"(after_mxcsr)
  :[original]"m"(original),[original_mxcsr]"m"(original_mxcsr)
  :"memory","st","st(1)","st(2)","st(3)","st(4)","st(5)","st(6)","st(7)");
 unsigned tags=unsigned(before[8])|(unsigned(before[9])<<8);
 require((before[4]&1)!=0,"dirty invalid flag");
 for(unsigned i=0;i<8;++i)require(((tags>>(i*2))&3)!=3,"eight occupied registers");
 require(std::memcmp(before,after,108)==0,"full x87 CW/SW/TOP/tags/registers differ");
 require(mxcsr==after_mxcsr,"MXCSR differs");
#else
 __asm__ volatile("mrs %0, fpcr; mrs %1, fpsr; msr fpcr, %2; msr fpsr, %3"
  :"=&r"(after_control),"=&r"(after_status):"r"(original_control),"r"(original_status):"memory");
 require(before_control==after_control&&before_status==after_status,"FPCR/FPSR differs");
#endif
 bool invalid=null_output||(precision!=24&&precision!=53);require(status==(invalid?-1:0),"state status");
 if(invalid)require(bits(out)==UINT64_C(0x123456789abcdef0),"invalid wrote output");
 ++state_cases;
}
int main(int argc,char**argv){
#if defined(__x86_64__)
 require(argc==4,"kernel.so frozen-oracle.so corpus.bin");
#else
 require(argc==3,"kernel.so corpus.bin");
#endif
 void*l=dlopen(argv[1],RTLD_NOW|RTLD_LOCAL);if(!l){std::fprintf(stderr,"%s\n",dlerror());return 1;}
 Kernel sw=sym<Kernel>(l,"software_degree_increment"),dispatch=sym<Kernel>(l,"legacy_degree_increment");
 auto select=sym<int(*)(int)>(l,"legacy_math_set_backend");
#if defined(__x86_64__)
 void*old=dlopen(argv[2],RTLD_NOW|RTLD_LOCAL);require(old,"frozen oracle dlopen");Raw raw(old);
 require(!dlsym(old,"software_degree_increment"),"oracle must predate degree kernel");
 Kernel hw=sym<Kernel>(l,"hardware_degree_increment");
 const std::initializer_list<Kernel> functions={sw,hw,dispatch};
 FILE*corpus=std::fopen(argv[3],"wb");
#else
 const std::initializer_list<Kernel> functions={sw,dispatch};
 require(select(2)==-2,"ARM64 hardware backend must be unsupported (-2)");
 require(!dlsym(l,"hardware_degree_increment"),"ARM64 must not export x87 kernel");
 FILE*corpus=std::fopen(argv[2],"rb");
#endif
 require(corpus,"open corpus");
 std::printf("GRAPH: getter (r/pi)*180 -> N64; rate FromSingle -> N64; add -> N64; setter (sum*pi)/180 -> T64. No normalizer, clamp, or zero-rate shortcut; NaN payloads compare all 8 bytes.\n");
 uint64_t special64[]={0,UINT64_C(0x8000000000000000),1,UINT64_C(0x8000000000000001),UINT64_C(0x000fffffffffffff),UINT64_C(0x0010000000000000),UINT64_C(0x7fefffffffffffff),UINT64_C(0xffefffffffffffff),UINT64_C(0x7ff0000000000000),UINT64_C(0xfff0000000000000),UINT64_C(0x7ff8000000001234),UINT64_C(0xfff8000000002345),UINT64_C(0x7ff0000000000001),UINT64_C(0xfff0000000000001),UINT64_C(0x3ff0000000000000),UINT64_C(0xbff0000000000000),UINT64_C(0x400921fb54442d18)};
 uint32_t special32[]={0,0x80000000,1,0x80000001,0x007fffff,0x00800000,0x7f7fffff,0xff7fffff,0x7f800000,0xff800000,0x7fc01234,0xffc12345,0x7f800001,0xff800001,0x3f800000,0xbf800000,0x3f000001};
 unsigned cases=0,mutants[2]={},zero_nonidentity=0;
 for(int p:{24,53}) for(unsigned i=0;i<200000;++i){
  uint64_t r=next();uint32_t d=uint32_t(next());
  if(i<289){r=special64[i/17];d=special32[i%17];}
  else if(i<30000){r=special64[next()%17];d=special32[next()%17];}
  else if(i<60000){r=(r&UINT64_C(0x800fffffffffffff))|(uint64_t(1020+next()%9)<<52);d=(d&0x807fffff)|uint32_t((120+next()%16)<<23);}
  else if(i<100000){r=(next()&1?UINT64_C(0x7fa0000000000000):UINT64_C(0x0010000000000000))+(next()%4097)-2048;d=uint32_t(next()&1?0:0x80000000);}
  double rad=cast<double>(r);float delta=cast<float>(d);uint64_t expected;
#if defined(__x86_64__)
  expected=raw.graph(rad,delta,p);
  require(std::fwrite(&expected,8,1,corpus)==1,"write corpus");
  if(i<100000)for(int mutation=1;mutation<=2;++mutation){
   uint64_t mutant=raw.graph(rad,delta,p,mutation);
   if(mutant!=expected){if(!mutants[mutation-1])std::printf("WITNESS omitted-%s-N64 PC=%d r=%016llx delta=%08x expected=%016llx mutant=%016llx\n",mutation==1?"degree":"sum",p,(unsigned long long)r,d,(unsigned long long)expected,(unsigned long long)mutant);++mutants[mutation-1];}
  }
#else
  require(std::fread(&expected,8,1,corpus)==1,"read independent frozen corpus");
#endif
  if((d&0x7fffffff)==0&&(r&UINT64_C(0x7ff0000000000000))!=UINT64_C(0x7ff0000000000000)&&expected!=r)++zero_nonidentity;
  for(Kernel fn:functions){double out;require(fn(rad,delta,p,&out)==0,"valid status");if(bits(out)!=expected){std::fprintf(stderr,"FAIL i=%u PC=%d r=%016llx d=%08x expected=%016llx actual=%016llx\n",i,p,(unsigned long long)r,d,(unsigned long long)expected,(unsigned long long)bits(out));return 1;}}
  for(int backend:{0,1,2}){
#if !defined(__x86_64__)
   if(backend==2)continue;
#endif
   require(select(backend)==0,"corpus backend select");double out;
   require(dispatch(rad,delta,p,&out)==0&&bits(out)==expected,"selected dispatch raw bits");
  }
  ++cases;
 }
#if defined(__x86_64__)
 require(mutants[0]==455,"frozen seed degree-N64 mutation count must remain 455");
 require(mutants[1]==0,"unexpected sum omission witness: inspect before updating gate");
 std::printf("UNPROVEN: sum-N64 omission has no sampled behavioral witness; static store-deletion rejection remains mandatory\n");
#else
 require(std::fgetc(corpus)==EOF,"extra corpus records");
#endif
 require(std::fclose(corpus)==0,"close corpus");require(zero_nonidentity>0,"zero-rate must not short-circuit getter/setter graph");
 for(int backend:{0,1,2}){
#if !defined(__x86_64__)
  if(backend==2)continue;
#endif
  require(select(backend)==0,"select");
  for(Kernel fn:functions)for(int p:{24,53,0,23,25,52,54,64,-1})for(bool null_output:{false,true})for(unsigned i:{0u,6u,12u})state(fn,p,null_output,special64[i],special32[i]);
  for(Kernel fn:functions)for(int p:{24,53}){double r=cast<double>(UINT64_C(0x400921fb54442d18)),out;require(sw(r,cast<float>(0x80000000u),p,&out)==0,"alias expected");require(fn(r,cast<float>(0x80000000u),p,&r)==0&&bits(r)==bits(out),"scalar alias");}
 }
 require(select(0)==0,"reset selector");
 std::printf("PASS %u exact 8-byte raw cases PC24/53; mutation witnesses=%u/%u; zero-rate nonidentity=%u; %u full-state/error cases; scalar alias\n",cases,mutants[0],mutants[1],zero_nonidentity,state_cases);
}
