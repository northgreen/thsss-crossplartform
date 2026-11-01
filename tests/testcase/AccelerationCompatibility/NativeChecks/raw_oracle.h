// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#pragma once
#include "../../DegreeCompatibility/NativeChecks/common.h"
#include <string>
struct Ext { uint64_t sig; uint16_t exp, pad[3]; };
static_assert(sizeof(Ext)==16);
using Kernel=int(*)(double,float,float,int,float*,float*);
using LD=int(*)(double,Ext*); using LF=int(*)(float,Ext*);
using SD=int(*)(const Ext*,double*); using SF=int(*)(const Ext*,float*);
using Bin=int(*)(const Ext*,const Ext*,int,Ext*); using Unary=int(*)(const Ext*,Ext*);
struct Pair { uint32_t x,y; };
struct Raw {
 LD ld; LF lf; SD sd; SF sf; Bin mul,div; Unary cos,sin;
 Raw(void*l,const char*backend){
  auto name=[&](const char*n){return std::string(backend)+"_ext_"+n;};
  ld=sym<LD>(l,name("load_double").c_str());lf=sym<LF>(l,name("load_float").c_str());
  sd=sym<SD>(l,name("store_double").c_str());sf=sym<SF>(l,name("store_float").c_str());
  mul=sym<Bin>(l,name("mul").c_str());div=sym<Bin>(l,name("div").c_str());
  cos=sym<Unary>(l,name("cos").c_str());sin=sym<Unary>(l,name("sin").c_str());
 }
 Ext n64(Ext x){double v;require(sd(&x,&v)==0&&ld(v,&x)==0,"raw N64");return x;}
 Ext n32(Ext x){float v;require(sf(&x,&v)==0&&lf(v,&x)==0,"raw N32");return x;}
 Pair graph(double direction,float ax,float ay,int p,int omit=0){
  Ext d,pi,deg,a,b,num,c,s,x,y;
  require(ld(direction,&d)==0&&lf(ax,&a)==0&&lf(ay,&b)==0,"raw input codecs");
  require(ld(cast<double>(UINT64_C(0x400921fb54442d18)),&pi)==0&&ld(180.0,&deg)==0,"binary64 constants");
  require(div(&d,&deg,p,&num)==0&&mul(&num,&pi,p,&num)==0,"raw extended num");
  Ext arg=omit==1?num:n64(num);
  require(cos(&arg,&c)==0,"raw Cos");if(omit!=2)c=n32(c);
  require(mul(&a,&c,p,&x)==0,"raw ax * N32(Cos)");
  arg=omit==1?num:n64(num);
  require(sin(&arg,&s)==0,"raw Sin");if(omit!=2)s=n32(s);
  require(mul(&b,&s,p,&y)==0,"raw ay * N32(Sin)");
  float ox,oy;require(sf(&x,&ox)==0&&sf(&y,&oy)==0,"raw final T32");
  return {cast<uint32_t>(ox),cast<uint32_t>(oy)};
 }
};
static bool same(Pair a,Pair b){return a.x==b.x&&a.y==b.y;}
static Pair invoke(Kernel fn,double d,float x,float y,int p){
 float a,b;require(fn(d,x,y,p,&a,&b)==0,"kernel valid status");return {cast<uint32_t>(a),cast<uint32_t>(b)};
}
