// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#include "state.h"
#include <initializer_list>
#include <chrono>
static uint64_t rng=0x71bca9938ff204adULL;
static uint64_t next(){rng^=rng<<13;rng^=rng>>7;rng^=rng<<17;return rng;}
static void compare(Pair expected,Pair actual,unsigned i,int p,int backend){
 if(same(expected,actual))return;
 std::fprintf(stderr,"FAIL i=%u PC=%d backend=%d expected=%08x/%08x actual=%08x/%08x\n",i,p,backend,expected.x,expected.y,actual.x,actual.y);std::exit(1);
}
#if defined(__x86_64__)
static void benchmark(Raw&raw,Kernel fn,const char*backend){
 volatile uint32_t sink=0;
 for(int round=0;round<4;++round){
  auto start=std::chrono::steady_clock::now();
  for(unsigned i=0;i<20000;++i){auto r=raw.graph(double(i%360),0.125f,-0.375f,53);sink=sink^r.x^r.y;}
  auto mid=std::chrono::steady_clock::now();
  for(unsigned i=0;i<20000;++i){auto r=invoke(fn,double(i%360),0.125f,-0.375f,53);sink=sink^r.x^r.y;}
  auto end=std::chrono::steady_clock::now();
  std::printf("CPU-COST %s round=%d old-six-raw=%.1f new-block=%.1f ns/input (includes codecs; not FPS) sink=%u\n",backend,round,std::chrono::duration<double,std::nano>(mid-start).count()/20000,std::chrono::duration<double,std::nano>(end-mid).count()/20000,sink);
 }
}
#endif
int main(int argc,char**argv){
#if defined(__x86_64__)
 require(argc==4,"candidate.so frozen-oracle.so corpus.bin");
#else
 require(argc==3,"candidate.so frozen-software-corpus.bin");
#endif
 void*l=dlopen(argv[1],RTLD_NOW|RTLD_LOCAL);if(!l){std::fprintf(stderr,"%s\n",dlerror());return 1;}
 Kernel sw=sym<Kernel>(l,"software_script_acceleration"),dispatch=sym<Kernel>(l,"legacy_script_acceleration");
 auto select=sym<int(*)(int)>(l,"legacy_math_set_backend");
 auto get=sym<int(*)()>(l,"legacy_math_get_backend");
#if defined(__x86_64__)
 void*old=dlopen(argv[2],RTLD_NOW|RTLD_LOCAL);require(old,"old raw oracle dlopen");
 require(!dlsym(old,"software_script_acceleration"),"oracle must predate acceleration");
 Raw raw_sw(old,"software"),raw_hw(old,"hardware");
 Kernel hw=sym<Kernel>(l,"hardware_script_acceleration");
 const std::initializer_list<Kernel> functions={sw,hw,dispatch};
 FILE*corpus=std::fopen(argv[3],"wb");
#else
 const std::initializer_list<Kernel> functions={sw,dispatch};
 require(select(1)==0&&select(2)==-2&&get()==1,"ARM backend2 refusal retains software");
 require(!dlsym(l,"hardware_script_acceleration"),"ARM must not export hardware acceleration");
 FILE*corpus=std::fopen(argv[2],"rb");
#endif
 require(corpus,"corpus open");
 const uint64_t special64[]={0,0x8000000000000000ULL,1,0x8000000000000001ULL,0x000fffffffffffffULL,0x0010000000000000ULL,0x7fefffffffffffffULL,0xffefffffffffffffULL,0x7ff0000000000000ULL,0xfff0000000000000ULL,0x7ff8000000001234ULL,0xfff8000000002345ULL,0x7ff0000000000001ULL,0xfff0000000000001ULL,0x4066800000000000ULL,0x400921fb54442d18ULL,0x43fca5dc1a63c1f8ULL};
 const uint32_t special32[]={0,0x80000000,1,0x80000001,0x007fffff,0x00800000,0x7f7fffff,0xff7fffff,0x7f800000,0xff800000,0x7fc01234,0xffc12345,0x7f800001,0xff800001,0x3f800000,0xbf800000,0x3f000001};
 unsigned cases=0,backend_differences=0,mutants[2]={};
 for(int p:{24,53})for(unsigned i=0;i<200000;++i){
  uint64_t d=next();uint32_t x=uint32_t(next()),y=uint32_t(next());
  if(i<4913){d=special64[i/289];x=special32[(i/17)%17];y=special32[i%17];}
  else if(i<30000){d=special64[next()%17];x=special32[next()%17];y=special32[next()%17];}
  else if(i<70000){d=(next()&1?0x43fca5dc1a63c1f8ULL:0x4066800000000000ULL)+next()%4097-2048;}
  else if(i<100000){d=(next()&0x800fffffffffffffULL)|(uint64_t(1020+next()%9)<<52);}
  if(x==y)y^=0x80000001u;
  double direction=cast<double>(d);float ax=cast<float>(x),ay=cast<float>(y);Pair expected;
#if defined(__x86_64__)
  expected=raw_sw.graph(direction,ax,ay,p);Pair hardware=raw_hw.graph(direction,ax,ay,p);
  require(std::fwrite(&expected,sizeof(expected),1,corpus)==1,"write software corpus");
  backend_differences+=!same(expected,hardware);
  compare(expected,invoke(sw,direction,ax,ay,p),i,p,1);compare(hardware,invoke(hw,direction,ax,ay,p),i,p,2);
  if(i<100000)for(int m=1;m<=2;++m)mutants[m-1]+=!same(expected,raw_sw.graph(direction,ax,ay,p,m));
#else
  require(std::fread(&expected,sizeof(expected),1,corpus)==1,"read frozen software corpus");
  compare(expected,invoke(sw,direction,ax,ay,p),i,p,1);
#endif
  for(int backend:{0,1,2}){
#if !defined(__x86_64__)
   if(backend==2)continue;
#endif
   require(select(backend)==0,"select backend");
   Pair dispatched=expected;
#if defined(__x86_64__)
   if(get()==2)dispatched=hardware;
#endif
   compare(dispatched,invoke(dispatch,direction,ax,ay,p),i,p,backend);
  }
  ++cases;
 }
#if defined(__x86_64__)
 std::printf("OBSERVED software/hardware final-output differences=%u; independently evaluated old raw chains (equality does not imply interchangeable trig backends)\n",backend_differences);
 std::printf("OMISSION-WITNESSES N64=%u N32=%u; zero witnesses do not waive exact static boundaries\n",mutants[0],mutants[1]);
 require(mutants[1]>0,"N32 behavioral omission witnesses required");
#else
 require(std::fgetc(corpus)==EOF,"extra corpus records");
#endif
 require(std::fclose(corpus)==0,"close corpus");
 for(int backend:{0,1,2}){
#if !defined(__x86_64__)
  if(backend==2)continue;
#endif
  require(select(backend)==0,"state select");
  for(Kernel fn:functions)for(int p:{24,53,0,23,25,52,54,64,-1})for(int outputs:{0,1,2,3})for(unsigned i:{0u,6u,12u})state(fn,p,outputs,special64[i],special32[i]);
  for(Kernel fn:functions)for(int p:{24,53})for(unsigned i=0;i<17;++i){
   double d=cast<double>(special64[i]);float x=cast<float>(special32[i]),y=cast<float>(special32[(i+1)%17]),alias;
   Pair expected=invoke(fn,d,x,y,p);require(fn(d,x,y,p,&alias,&alias)==0&&cast<uint32_t>(alias)==expected.y,"aliased output must receive Y last");
  }
 }
 require(select(0)==0,"reset backend");
 std::printf("PASS %u fixed cases PC24/53, both float bit patterns direct/dispatched; %u state/error cases; Y-last alias; backend differences=%u N64/N32 witnesses=%u/%u\n",cases,state_cases,backend_differences,mutants[0],mutants[1]);
#if defined(__x86_64__)
 benchmark(raw_sw,sw,"software");benchmark(raw_hw,hw,"hardware");
#endif
}
