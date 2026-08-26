#include "ExplorerCommandContract.h"

#include <windows.h>
#include <shlwapi.h>
#include <shobjidl_core.h>

#include <atomic>
#include <cstdint>
#include <cwchar>
#include <iterator>
#include <new>

namespace
{
    HMODULE moduleHandle = nullptr;
    std::atomic_ulong serverReferenceCount = 0;

    class ServerReference final
    {
    public:
        ServerReference() noexcept
        {
            serverReferenceCount.fetch_add(1, std::memory_order_relaxed);
        }

        ~ServerReference()
        {
            serverReferenceCount.fetch_sub(1, std::memory_order_relaxed);
        }
    };

    HRESULT DuplicateString(const wchar_t* value, PWSTR* destination) noexcept
    {
        if (destination == nullptr)
        {
            return E_POINTER;
        }

        *destination = nullptr;
        return SHStrDupW(value, destination);
    }

    std::uint64_t GetUnixTimeMilliseconds() noexcept
    {
        FILETIME fileTime{};
        GetSystemTimeAsFileTime(&fileTime);
        ULARGE_INTEGER ticks{};
        ticks.LowPart = fileTime.dwLowDateTime;
        ticks.HighPart = fileTime.dwHighDateTime;
        constexpr std::uint64_t windowsToUnixEpochTicks = 116444736000000000ULL;
        return (ticks.QuadPart - windowsToUnixEpochTicks) / 10000ULL;
    }

    bool FormatNonce(const GUID& nonce, wchar_t (&value)[33]) noexcept
    {
        const int written = swprintf_s(
            value,
            L"%08x%04x%04x%02x%02x%02x%02x%02x%02x%02x%02x",
            nonce.Data1,
            nonce.Data2,
            nonce.Data3,
            nonce.Data4[0],
            nonce.Data4[1],
            nonce.Data4[2],
            nonce.Data4[3],
            nonce.Data4[4],
            nonce.Data4[5],
            nonce.Data4[6],
            nonce.Data4[7]);
        return written == 32;
    }

    HRESULT BuildActivationArguments(wchar_t (&arguments)[257]) noexcept
    {
        POINT cursor{};
        if (!GetCursorPos(&cursor))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        GUID nonce{};
        HRESULT result = CoCreateGuid(&nonce);
        if (FAILED(result))
        {
            return result;
        }

        wchar_t nonceValue[33]{};
        if (!FormatNonce(nonce, nonceValue))
        {
            return E_UNEXPECTED;
        }

        const int written = swprintf_s(
            arguments,
            L"--long-grid-create-box=v1,%ld,%ld,%llu,%ls",
            cursor.x,
            cursor.y,
            static_cast<unsigned long long>(GetUnixTimeMilliseconds()),
            nonceValue);
        if (written <= 0 || written >= static_cast<int>(std::size(arguments)))
        {
            return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
        }

        return S_OK;
    }

    class ExplorerCommand final : public IExplorerCommand
    {
    public:
        ExplorerCommand() noexcept = default;

        IFACEMETHODIMP QueryInterface(REFIID interfaceId, void** value) noexcept override
        {
            if (value == nullptr)
            {
                return E_POINTER;
            }

            *value = nullptr;
            if (IsEqualIID(interfaceId, IID_IUnknown)
                || IsEqualIID(interfaceId, IID_IExplorerCommand))
            {
                *value = static_cast<IExplorerCommand*>(this);
                AddRef();
                return S_OK;
            }

            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() noexcept override
        {
            return referenceCount.fetch_add(1, std::memory_order_relaxed) + 1;
        }

        IFACEMETHODIMP_(ULONG) Release() noexcept override
        {
            const ULONG remaining =
                referenceCount.fetch_sub(1, std::memory_order_acq_rel) - 1;
            if (remaining == 0)
            {
                delete this;
            }
            return remaining;
        }

        IFACEMETHODIMP GetTitle(IShellItemArray*, PWSTR* title) noexcept override
        {
            return DuplicateString(LongGridExplorerCommandTitle, title);
        }

        IFACEMETHODIMP GetIcon(IShellItemArray*, PWSTR* icon) noexcept override
        {
            if (icon == nullptr)
            {
                return E_POINTER;
            }

            wchar_t modulePath[MAX_PATH]{};
            const DWORD length = GetModuleFileNameW(
                moduleHandle,
                modulePath,
                static_cast<DWORD>(std::size(modulePath)));
            if (length == 0 || length >= std::size(modulePath))
            {
                *icon = nullptr;
                return HRESULT_FROM_WIN32(
                    length == 0 ? GetLastError() : ERROR_INSUFFICIENT_BUFFER);
            }

            wchar_t iconLocation[MAX_PATH + 16]{};
            const int written = swprintf_s(
                iconLocation,
                L"%ls,-%d",
                modulePath,
                LongGridExplorerCommandIconResourceId);
            if (written <= 0
                || written >= static_cast<int>(std::size(iconLocation)))
            {
                *icon = nullptr;
                return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
            }
            return DuplicateString(iconLocation, icon);
        }

        IFACEMETHODIMP GetToolTip(IShellItemArray*, PWSTR* tooltip) noexcept override
        {
            if (tooltip == nullptr)
            {
                return E_POINTER;
            }
            *tooltip = nullptr;
            return E_NOTIMPL;
        }

        IFACEMETHODIMP GetCanonicalName(GUID* canonicalName) noexcept override
        {
            if (canonicalName == nullptr)
            {
                return E_POINTER;
            }
            *canonicalName = CLSID_LongGridExplorerCommand;
            return S_OK;
        }

        IFACEMETHODIMP GetState(
            IShellItemArray*,
            BOOL,
            EXPCMDSTATE* state) noexcept override
        {
            if (state == nullptr)
            {
                return E_POINTER;
            }
            *state = ECS_ENABLED;
            return S_OK;
        }

        IFACEMETHODIMP Invoke(IShellItemArray*, IBindCtx*) noexcept override
        {
            wchar_t arguments[257]{};
            HRESULT result = BuildActivationArguments(arguments);
            if (FAILED(result))
            {
                return result;
            }

            IApplicationActivationManager* activationManager = nullptr;
            result = CoCreateInstance(
                CLSID_ApplicationActivationManager,
                nullptr,
                CLSCTX_INPROC_SERVER,
                IID_PPV_ARGS(&activationManager));
            if (FAILED(result))
            {
                return result;
            }

            DWORD processId = 0;
            result = activationManager->ActivateApplication(
                LongGridApplicationUserModelId,
                arguments,
                AO_NONE,
                &processId);
            activationManager->Release();
            return result;
        }

        IFACEMETHODIMP GetFlags(EXPCMDFLAGS* flags) noexcept override
        {
            if (flags == nullptr)
            {
                return E_POINTER;
            }
            *flags = ECF_DEFAULT;
            return S_OK;
        }

        IFACEMETHODIMP EnumSubCommands(
            IEnumExplorerCommand** commands) noexcept override
        {
            if (commands == nullptr)
            {
                return E_POINTER;
            }
            *commands = nullptr;
            return E_NOTIMPL;
        }

    private:
        ~ExplorerCommand() = default;

        ServerReference serverReference;
        std::atomic_ulong referenceCount = 1;
    };

    class ExplorerCommandFactory final : public IClassFactory
    {
    public:
        ExplorerCommandFactory() noexcept = default;

        IFACEMETHODIMP QueryInterface(REFIID interfaceId, void** value) noexcept override
        {
            if (value == nullptr)
            {
                return E_POINTER;
            }

            *value = nullptr;
            if (IsEqualIID(interfaceId, IID_IUnknown)
                || IsEqualIID(interfaceId, IID_IClassFactory))
            {
                *value = static_cast<IClassFactory*>(this);
                AddRef();
                return S_OK;
            }
            return E_NOINTERFACE;
        }

        IFACEMETHODIMP_(ULONG) AddRef() noexcept override
        {
            return referenceCount.fetch_add(1, std::memory_order_relaxed) + 1;
        }

        IFACEMETHODIMP_(ULONG) Release() noexcept override
        {
            const ULONG remaining =
                referenceCount.fetch_sub(1, std::memory_order_acq_rel) - 1;
            if (remaining == 0)
            {
                delete this;
            }
            return remaining;
        }

        IFACEMETHODIMP CreateInstance(
            IUnknown* outer,
            REFIID interfaceId,
            void** value) noexcept override
        {
            if (value == nullptr)
            {
                return E_POINTER;
            }
            *value = nullptr;
            if (outer != nullptr)
            {
                return CLASS_E_NOAGGREGATION;
            }

            ExplorerCommand* command = new (std::nothrow) ExplorerCommand();
            if (command == nullptr)
            {
                return E_OUTOFMEMORY;
            }
            const HRESULT result = command->QueryInterface(interfaceId, value);
            command->Release();
            return result;
        }

        IFACEMETHODIMP LockServer(BOOL lock) noexcept override
        {
            if (lock)
            {
                serverReferenceCount.fetch_add(1, std::memory_order_relaxed);
            }
            else
            {
                serverReferenceCount.fetch_sub(1, std::memory_order_relaxed);
            }
            return S_OK;
        }

    private:
        ~ExplorerCommandFactory() = default;

        ServerReference serverReference;
        std::atomic_ulong referenceCount = 1;
    };
}

extern "C" BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, void*)
{
    if (reason == DLL_PROCESS_ATTACH)
    {
        moduleHandle = instance;
        DisableThreadLibraryCalls(instance);
    }
    return TRUE;
}

extern "C" HRESULT __stdcall DllGetClassObject(
    REFCLSID classId,
    REFIID interfaceId,
    void** value)
{
    if (!IsEqualCLSID(classId, CLSID_LongGridExplorerCommand))
    {
        return CLASS_E_CLASSNOTAVAILABLE;
    }
    if (value == nullptr)
    {
        return E_POINTER;
    }

    ExplorerCommandFactory* factory =
        new (std::nothrow) ExplorerCommandFactory();
    if (factory == nullptr)
    {
        *value = nullptr;
        return E_OUTOFMEMORY;
    }
    const HRESULT result = factory->QueryInterface(interfaceId, value);
    factory->Release();
    return result;
}

extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    return serverReferenceCount.load(std::memory_order_acquire) == 0
        ? S_OK
        : S_FALSE;
}
