#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <stdint.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include <wchar.h>

typedef struct { volatile LONG stage; LONG token; wchar_t detail[1024]; } Status;
typedef int (__stdcall *clr_initialize)(const char*, const char*, int, const char**, const char**, void**, unsigned*);
typedef int (__stdcall *clr_delegate)(void*, unsigned, const char*, const char*, const char*, void**);
typedef int (__stdcall *managed_entry)(void);
static HMODULE self_module;
static volatile LONG started;

static void fail(Status* s, const wchar_t* text) {
    _snwprintf_s(s->detail, 1024, _TRUNCATE, L"%ls (Win32 %lu)", text, GetLastError());
    InterlockedExchange(&s->stage, -1);
}
static char* utf8(const wchar_t* value) {
    int length = WideCharToMultiByte(CP_UTF8, 0, value, -1, NULL, 0, NULL, NULL);
    char* result = (char*)calloc((size_t)length, 1);
    if (result) WideCharToMultiByte(CP_UTF8, 0, value, -1, result, length, NULL, NULL);
    return result;
}
static int append(char** list, const wchar_t* path) {
    char* item = utf8(path);
    if (!item) return 0;
    size_t old = *list ? strlen(*list) : 0;
    char* expanded = (char*)realloc(*list, old + strlen(item) + 2);
    if (!expanded) { free(item); return 0; }
    *list = expanded;
    strcpy(expanded + old, item); strcat(expanded, ";"); free(item); return 1;
}
static int add_dlls(char** list, const wchar_t* folder) {
    wchar_t pattern[32768], path[32768];
    _snwprintf_s(pattern, 32768, _TRUNCATE, L"%ls\\*.dll", folder);
    WIN32_FIND_DATAW data; HANDLE find = FindFirstFileW(pattern, &data);
    if (find == INVALID_HANDLE_VALUE) return 0;
    int ok = 1;
    do {
        if (!(data.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY)) {
            _snwprintf_s(path, 32768, _TRUNCATE, L"%ls\\%ls", folder, data.cFileName);
            if (!append(list, path)) { ok = 0; break; }
        }
    } while (FindNextFileW(find, &data));
    FindClose(find); return ok;
}

static void initialize(Status* s) {
    if (GetModuleHandleW(L"coreclr.dll")) { fail(s, L"CoreCLR already loaded; duplicate initialization refused"); return; }
    HMODULE game = GetModuleHandleW(L"GameAssembly.dll");
    if (!game) { fail(s, L"GameAssembly is not loaded"); return; }
    typedef void* (__cdecl *domain_get)(void);
    domain_get get_domain = (domain_get)(void*)GetProcAddress(game, "il2cpp_domain_get");
    if (!get_domain || !get_domain()) { fail(s, L"IL2CPP is not initialized; wait for the main menu"); return; }
    wchar_t exe[32768], root[32768], dll[32768], boot[32768], runtime[32768], core[32768];
    if (!GetModuleFileNameW(NULL, exe, 32768) || !GetModuleFileNameW(self_module, boot, 32768)) { fail(s, L"Path lookup failed"); return; }
    wcscpy_s(root, 32768, exe); wchar_t* slash = wcsrchr(root, L'\\'); if (!slash) { fail(s,L"Invalid game path"); return; } *slash = 0;
    slash = wcsrchr(boot, L'\\'); if (!slash) { fail(s,L"Invalid bootstrap path"); return; } *slash = 0;
    _snwprintf_s(runtime, 32768, _TRUNCATE, L"%ls\\dotnet", root);
    _snwprintf_s(core, 32768, _TRUNCATE, L"%ls\\BepInEx\\core", root);
    _snwprintf_s(dll, 32768, _TRUNCATE, L"%ls\\coreclr.dll", runtime);
    HMODULE clr = LoadLibraryExW(dll, NULL, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_DEFAULT_DIRS);
    if (!clr) { fail(s, L"Cannot load packaged CoreCLR"); return; }
    clr_initialize init = (clr_initialize)(void*)GetProcAddress(clr, "coreclr_initialize");
    clr_delegate create_delegate = (clr_delegate)(void*)GetProcAddress(clr, "coreclr_create_delegate");
    if (!init || !create_delegate) { fail(s, L"Missing CoreCLR hosting exports"); return; }
    char *tpa = NULL, *app = NULL, *native = NULL, *exe8 = utf8(exe), *root8 = utf8(root);
    if (!exe8 || !root8 || !add_dlls(&tpa, runtime) || !add_dlls(&tpa, boot) ||
        !append(&app, boot) || !append(&app, core) || !append(&native, root) || !append(&native, runtime) || !append(&native, core)) {
        fail(s, L"Cannot prepare CoreCLR search paths"); goto cleanup;
    }
    const char* keys[] = { "TRUSTED_PLATFORM_ASSEMBLIES", "APP_PATHS", "NATIVE_DLL_SEARCH_DIRECTORIES", "APP_CONTEXT_BASE_DIRECTORY" };
    const char* values[] = { tpa, app, native, root8 };
    void* host = NULL; unsigned domain = 0;
    int hr = init(exe8, "NightsHack.Dynamic", 4, keys, values, &host, &domain);
    if (hr < 0) { _snwprintf_s(s->detail,1024,_TRUNCATE,L"coreclr_initialize failed: 0x%08X",(unsigned)hr); InterlockedExchange(&s->stage,-1); goto cleanup; }
    managed_entry entry = NULL;
    hr = create_delegate(host, domain, "NightsHack.Bootstrap", "NightsHack.Bootstrap.Entry", "Run", (void**)&entry);
    if (hr < 0 || !entry) { _snwprintf_s(s->detail,1024,_TRUNCATE,L"coreclr_create_delegate failed: 0x%08X",(unsigned)hr); InterlockedExchange(&s->stage,-1); goto cleanup; }
    // Keep CoreCLR and this bootstrap resident, but no periodic worker or native method detour remains.
    InterlockedExchange(&s->stage, entry() == 0 ? 2 : -1);
cleanup:
    free(tpa); free(app); free(native); free(exe8); free(root8);
}

__declspec(dllexport) LRESULT CALLBACK NightsHackMessageHook(int code, WPARAM wParam, LPARAM lParam) {
    if (code >= 0 && wParam == PM_REMOVE) {
        MSG* message = (MSG*)lParam;
        if (message->message == RegisterWindowMessageW(L"NightsHack.Inject.v1")) {
            wchar_t name[96]; _snwprintf_s(name,96,_TRUNCATE,L"Local\\NightsHack.Inject.%lu",GetCurrentProcessId());
            HANDLE mapping = OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name);
            if (mapping) {
                Status* s = (Status*)MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,4096);
                if (s && message->wParam == (WPARAM)s->token && InterlockedCompareExchange(&s->stage,1,0) == 0) {
                    if (InterlockedCompareExchange(&started,1,0) == 0) {
                        HMODULE pinned; GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_PIN,(LPCWSTR)&NightsHackMessageHook,&pinned);
                        initialize(s);
                    } else fail(s,L"Bootstrap already attempted; restart before retrying");
                }
                if (s) UnmapViewOfFile(s); CloseHandle(mapping);
            }
        }
    }
    return CallNextHookEx(NULL, code, wParam, lParam);
}
BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID reserved) {
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) { self_module = module; DisableThreadLibraryCalls(module); }
    return TRUE;
}
