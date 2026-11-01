// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#pragma once
#include <dlfcn.h>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
static inline void require(bool b,const char*m){if(!b){std::fprintf(stderr,"FAIL %s\n",m);std::exit(1);}}
template<class T> static T sym(void*l,const char*n){auto p=dlsym(l,n);require(p,n);return reinterpret_cast<T>(p);}
template<class T,class U> static T cast(U word){static_assert(sizeof(T)==sizeof(U));T v;std::memcpy(&v,&word,sizeof(v));return v;}
static inline uint64_t bits(double v){return cast<uint64_t>(v);}
