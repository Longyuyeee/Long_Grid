#pragma once

#include <windows.h>
#include <appmodel.h>

inline constexpr GUID CLSID_LongGridExplorerCommand = {
    0x78a940c1,
    0x2e65,
    0x4a03,
    {0x9d, 0x09, 0x3a, 0xc6, 0x2c, 0xef, 0x30, 0xbb}};

inline constexpr wchar_t LongGridApplicationId[] = L"LongGrid.App";

// Use the installed package identity, including its publisher ID. Never guess
// a package family from the manifest Name or fall back to an unpackaged launch.
inline LONG BuildLongGridApplicationUserModelId(
    UINT32* length,
    PWSTR applicationUserModelId,
    decltype(&GetCurrentPackageFamilyName) getFamily = GetCurrentPackageFamilyName) noexcept
{
    wchar_t family[PACKAGE_FAMILY_NAME_MAX_LENGTH + 1]{};
    UINT32 familyLength = static_cast<UINT32>(sizeof(family) / sizeof(family[0]));
    const LONG result = getFamily(&familyLength, family);
    if (result != ERROR_SUCCESS)
    {
        return result;
    }
    return FormatApplicationUserModelId(family, LongGridApplicationId, length, applicationUserModelId);
}
inline constexpr wchar_t LongGridExplorerCommandTitle[] =
    L"新建 Long方格盒子";
inline constexpr int LongGridExplorerCommandIconResourceId = 101;
