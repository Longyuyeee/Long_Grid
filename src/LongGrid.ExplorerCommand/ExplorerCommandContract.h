#pragma once

#include <guiddef.h>

inline constexpr GUID CLSID_LongGridExplorerCommand = {
    0x78a940c1,
    0x2e65,
    0x4a03,
    {0x9d, 0x09, 0x3a, 0xc6, 0x2c, 0xef, 0x30, 0xbb}};

inline constexpr wchar_t LongGridApplicationUserModelId[] =
    L"Longyuyeee.LongGrid.DeveloperPreview!LongGrid.App";
inline constexpr wchar_t LongGridExplorerCommandTitle[] =
    L"新建 Long方格盒子";
inline constexpr int LongGridExplorerCommandIconResourceId = 101;
