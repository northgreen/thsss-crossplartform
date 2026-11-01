// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
// Only the frozen pre-degree library's primitive software API is used here.
#pragma once
#include "common.h"
struct Ext { uint64_t sig; uint16_t exp, pad[3]; };
static_assert(sizeof(Ext)==16);
using LD=int(*)(double,Ext*); using LF=int(*)(float,Ext*);
using SD=int(*)(const Ext*,double*); using Bin=int(*)(const Ext*,const Ext*,int,Ext*);
struct Raw {
 LD ld; LF lf; SD sd; Bin add,mul,div;
 explicit Raw(void*l){ld=sym<LD>(l,"software_ext_load_double");lf=sym<LF>(l,"software_ext_load_float");sd=sym<SD>(l,"software_ext_store_double");add=sym<Bin>(l,"software_ext_add");mul=sym<Bin>(l,"software_ext_mul");div=sym<Bin>(l,"software_ext_div");}
 Ext n64(Ext x){double v;require(sd(&x,&v)==0,"store");require(ld(v,&x)==0,"reload");return x;}
 uint64_t graph(double r,float rate,int p,int omit=0){
  Ext x,delta,pi,deg,d,s,o;
  require(ld(r,&x)==0&&lf(rate,&delta)==0,"input codecs");
  require(ld(cast<double>(UINT64_C(0x400921fb54442d18)),&pi)==0,"pi codec");
  require(ld(cast<double>(UINT64_C(0x4066800000000000)),&deg)==0,"180 codec");
  require(div(&x,&pi,p,&d)==0&&mul(&d,&deg,p,&d)==0,"Degree getter divide/multiply");
  if(omit!=1)d=n64(d);
  delta=n64(delta); // Ctrl's FromSingle -> N64, not host float arithmetic.
  require(add(&d,&delta,p,&s)==0,"Ctrl add");
  if(omit!=2)s=n64(s);
  require(mul(&s,&pi,p,&o)==0&&div(&o,&deg,p,&o)==0,"Degree setter multiply/divide");
  double out;require(sd(&o,&out)==0,"final T64");return bits(out);
 }
};
