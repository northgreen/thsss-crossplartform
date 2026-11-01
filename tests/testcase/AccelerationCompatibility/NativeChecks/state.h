// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#pragma once
#include "raw_oracle.h"
static unsigned state_cases;
static void state(Kernel fn,int precision,int outputs,uint64_t direction,uint32_t acceleration){
 float x=cast<float>(0x12345678u),y=cast<float>(0x23456789u);
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
 int status=fn(cast<double>(direction),cast<float>(acceleration),cast<float>(acceleration^0x80012345u),precision,outputs&1?nullptr:&x,outputs&2?nullptr:&y);
#if defined(__x86_64__)
 __asm__ volatile(
  "fnsave %[after]; stmxcsr %[after_mxcsr]; frstor %[original]; ldmxcsr %[original_mxcsr]"
  :[after]"=m"(after),[after_mxcsr]"=m"(after_mxcsr)
  :[original]"m"(original),[original_mxcsr]"m"(original_mxcsr)
  :"memory","st","st(1)","st(2)","st(3)","st(4)","st(5)","st(6)","st(7)");
 unsigned tags=unsigned(before[8])|(unsigned(before[9])<<8);
 require((before[4]&1)!=0,"dirty invalid flag");
 for(unsigned i=0;i<8;++i)require(((tags>>(i*2))&3)!=3,"eight occupied registers");
 require(std::memcmp(before,after,108)==0,"full x87 CW/SW/TOP/tags/eight registers differ");
 require(mxcsr==after_mxcsr,"MXCSR differs");
#else
 __asm__ volatile("mrs %0, fpcr; mrs %1, fpsr; msr fpcr, %2; msr fpsr, %3"
  :"=&r"(after_control),"=&r"(after_status):"r"(original_control),"r"(original_status):"memory");
 require(before_control==after_control&&before_status==after_status,"FPCR/FPSR differs");
#endif
 bool invalid=outputs||(precision!=24&&precision!=53);require(status==(invalid?-1:0),"state status");
 if(invalid)require(cast<uint32_t>(x)==0x12345678u&&cast<uint32_t>(y)==0x23456789u,"invalid wrote either output");
 ++state_cases;
}
