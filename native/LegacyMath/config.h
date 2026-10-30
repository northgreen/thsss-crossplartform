// SPDX-License-Identifier: LGPL-2.1-or-later
// Copyright (C) 2026 THSSS contributors.
#pragma once
#include <stdint.h>
#if defined(__BYTE_ORDER__) && __BYTE_ORDER__ == __ORDER_BIG_ENDIAN__
#define BX_BIG_ENDIAN 1
#else
#define BX_LITTLE_ENDIAN 1
#endif
#define BX_CPP_INLINE inline
#define BX_CONST64(x) UINT64_C(x)
#define BX_HAVE___BUILTIN_CLZ 0
#define BX_HAVE_INT128 0
using Bit8u = uint8_t;
using Bit8s = int8_t;
using Bit16u = uint16_t;
using Bit16s = int16_t;
using Bit32u = uint32_t;
using Bit32s = int32_t;
using Bit64u = uint64_t;
using Bit64s = int64_t;
