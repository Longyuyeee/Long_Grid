#include "..\..\src\LongGrid.ExplorerCommand\ExplorerCommandContract.h"

#include <windows.h>
#include <shobjidl_core.h>

#include <chrono>
#include <iostream>
#include <string>

namespace
{
    using DllGetClassObjectFunction = HRESULT(__stdcall*)(
        REFCLSID,
        REFIID,
        void**);
    using DllCanUnloadNowFunction = HRESULT(__stdcall*)();

    bool Contains(const std::wstring& value, const std::wstring& expected)
    {
        return value.find(expected) != std::wstring::npos;
    }
}

int wmain(int argumentCount, wchar_t** arguments)
{
    if (argumentCount != 2)
    {
        std::cerr << "Expected the Explorer command DLL path." << std::endl;
        return 2;
    }

    const HRESULT initializeResult =
        CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(initializeResult))
    {
        std::cerr << "COM initialization failed." << std::endl;
        return 3;
    }

    HMODULE module = LoadLibraryExW(
        arguments[1],
        nullptr,
        LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (module == nullptr)
    {
        CoUninitialize();
        std::cerr << "Explorer command DLL could not be loaded." << std::endl;
        return 4;
    }

    auto getClassObject = reinterpret_cast<DllGetClassObjectFunction>(
        GetProcAddress(module, "DllGetClassObject"));
    auto canUnloadNow = reinterpret_cast<DllCanUnloadNowFunction>(
        GetProcAddress(module, "DllCanUnloadNow"));
    if (getClassObject == nullptr || canUnloadNow == nullptr)
    {
        FreeLibrary(module);
        CoUninitialize();
        std::cerr << "Required COM exports are missing." << std::endl;
        return 5;
    }

    IClassFactory* factory = nullptr;
    HRESULT result = getClassObject(
        CLSID_LongGridExplorerCommand,
        IID_PPV_ARGS(&factory));
    if (FAILED(result) || factory == nullptr)
    {
        FreeLibrary(module);
        CoUninitialize();
        std::cerr << "COM class factory creation failed." << std::endl;
        return 6;
    }

    IExplorerCommand* command = nullptr;
    result = factory->CreateInstance(
        nullptr,
        IID_PPV_ARGS(&command));
    if (FAILED(result) || command == nullptr)
    {
        factory->Release();
        FreeLibrary(module);
        CoUninitialize();
        std::cerr << "IExplorerCommand creation failed." << std::endl;
        return 7;
    }

    DWORD handlesBefore = 0;
    GetProcessHandleCount(GetCurrentProcess(), &handlesBefore);
    const auto started = std::chrono::steady_clock::now();
    bool titlePassed = true;
    bool iconPassed = true;
    bool statePassed = true;
    bool canonicalNamePassed = true;
    bool flagsPassed = true;
    bool subcommandsPassed = true;
    constexpr int iterations = 200;
    for (int index = 0; index < iterations; ++index)
    {
        PWSTR title = nullptr;
        titlePassed = titlePassed
            && SUCCEEDED(command->GetTitle(nullptr, &title))
            && title != nullptr
            && std::wstring(title) == LongGridExplorerCommandTitle;
        CoTaskMemFree(title);

        PWSTR icon = nullptr;
        iconPassed = iconPassed
            && SUCCEEDED(command->GetIcon(nullptr, &icon))
            && icon != nullptr
            && Contains(icon, L"LongGrid.ExplorerCommand.dll,-101");
        CoTaskMemFree(icon);

        EXPCMDSTATE state = ECS_HIDDEN;
        statePassed = statePassed
            && SUCCEEDED(command->GetState(nullptr, FALSE, &state))
            && state == ECS_ENABLED;

        GUID canonicalName{};
        canonicalNamePassed = canonicalNamePassed
            && SUCCEEDED(command->GetCanonicalName(&canonicalName))
            && IsEqualGUID(canonicalName, CLSID_LongGridExplorerCommand);

        EXPCMDFLAGS flags = ECF_HASSUBCOMMANDS;
        flagsPassed = flagsPassed
            && SUCCEEDED(command->GetFlags(&flags))
            && flags == ECF_DEFAULT;

        IEnumExplorerCommand* subcommands = nullptr;
        subcommandsPassed = subcommandsPassed
            && command->EnumSubCommands(&subcommands) == E_NOTIMPL
            && subcommands == nullptr;
    }
    const auto elapsedMilliseconds =
        std::chrono::duration_cast<std::chrono::milliseconds>(
            std::chrono::steady_clock::now() - started).count();
    DWORD handlesAfter = 0;
    GetProcessHandleCount(GetCurrentProcess(), &handlesAfter);

    command->Release();
    factory->Release();
    const bool unloadPassed = canUnloadNow() == S_OK;
    const bool bounded = elapsedMilliseconds <= 1000;
    const bool handlesStable = handlesAfter <= handlesBefore + 2;
    const bool passed = titlePassed
        && iconPassed
        && statePassed
        && canonicalNamePassed
        && flagsPassed
        && subcommandsPassed
        && unloadPassed
        && bounded
        && handlesStable;

    std::cout
        << "{\n"
        << "  \"SchemaVersion\": 1,\n"
        << "  \"Purpose\": \"BoxR1ExplorerCommandNativeProbe\",\n"
        << "  \"Iterations\": " << iterations << ",\n"
        << "  \"ElapsedMilliseconds\": " << elapsedMilliseconds << ",\n"
        << "  \"TitlePassed\": " << (titlePassed ? "true" : "false") << ",\n"
        << "  \"IconPassed\": " << (iconPassed ? "true" : "false") << ",\n"
        << "  \"StatePassed\": " << (statePassed ? "true" : "false") << ",\n"
        << "  \"CanonicalNamePassed\": " << (canonicalNamePassed ? "true" : "false") << ",\n"
        << "  \"FlagsPassed\": " << (flagsPassed ? "true" : "false") << ",\n"
        << "  \"SubcommandsPassed\": " << (subcommandsPassed ? "true" : "false") << ",\n"
        << "  \"UnloadPassed\": " << (unloadPassed ? "true" : "false") << ",\n"
        << "  \"HandlesBefore\": " << handlesBefore << ",\n"
        << "  \"HandlesAfter\": " << handlesAfter << ",\n"
        << "  \"Outcome\": \"" << (passed ? "Pass" : "Fail") << "\"\n"
        << "}\n";

    FreeLibrary(module);
    CoUninitialize();
    return passed ? 0 : 8;
}
