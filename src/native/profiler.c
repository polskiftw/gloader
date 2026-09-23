#define _GNU_SOURCE

#include <dlfcn.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <unistd.h>

#ifndef PATH_MAX
#define PATH_MAX 4096
#endif

typedef struct _MonoProfiler MonoProfiler;
typedef struct _MonoProfilerDesc *MonoProfilerHandle;
typedef struct _MonoDomain MonoDomain;
typedef struct _MonoAssembly MonoAssembly;
typedef struct _MonoAssemblyName MonoAssemblyName;
typedef struct _MonoImage MonoImage;
typedef struct _MonoClass MonoClass;
typedef struct _MonoMethod MonoMethod;
typedef struct _MonoObject MonoObject;
typedef struct _MonoString MonoString;

typedef MonoProfilerHandle (*mono_profiler_create_fn)(MonoProfiler *);
typedef void (*assembly_loaded_callback_fn)(MonoProfiler *, MonoAssembly *);
typedef void (*mono_profiler_set_assembly_loaded_callback_fn)(MonoProfilerHandle, assembly_loaded_callback_fn);
typedef MonoDomain *(*mono_get_root_domain_fn)(void);
typedef MonoAssembly *(*mono_domain_assembly_open_fn)(MonoDomain *, const char *);
typedef MonoAssemblyName *(*mono_assembly_get_name_fn)(MonoAssembly *);
typedef const char *(*mono_assembly_name_get_name_fn)(MonoAssemblyName *);
typedef MonoImage *(*mono_assembly_get_image_fn)(MonoAssembly *);
typedef MonoClass *(*mono_class_from_name_fn)(MonoImage *, const char *, const char *);
typedef MonoMethod *(*mono_class_get_method_from_name_fn)(MonoClass *, const char *, int);
typedef MonoObject *(*mono_runtime_invoke_fn)(MonoMethod *, void *, void **, MonoObject **);
typedef void *(*mono_object_unbox_fn)(MonoObject *);
typedef MonoString *(*mono_object_to_string_fn)(MonoObject *, MonoObject **);
typedef char *(*mono_string_to_utf8_fn)(MonoString *);
typedef void (*mono_free_fn)(void *);
typedef void (*mono_set_assemblies_path_fn)(const char *);

struct mono_api {
    mono_profiler_create_fn profiler_create;
    mono_profiler_set_assembly_loaded_callback_fn set_assembly_loaded_callback;
    mono_get_root_domain_fn get_root_domain;
    mono_domain_assembly_open_fn domain_assembly_open;
    mono_assembly_get_name_fn assembly_get_name;
    mono_assembly_name_get_name_fn assembly_name_get_name;
    mono_assembly_get_image_fn assembly_get_image;
    mono_class_from_name_fn class_from_name;
    mono_class_get_method_from_name_fn class_get_method_from_name;
    mono_runtime_invoke_fn runtime_invoke;
    mono_object_unbox_fn object_unbox;
    mono_object_to_string_fn object_to_string;
    mono_string_to_utf8_fn string_to_utf8;
    mono_free_fn free_memory;
    mono_set_assemblies_path_fn set_assemblies_path;
};

static struct mono_api api;
static int initialized;
static int initializing;

static int join_path(char *out, size_t size, const char *left, const char *right)
{
    const int written = snprintf(out, size, "%s/%s", left, right);
    return written >= 0 && (size_t)written < size;
}

static int load_symbol(const char *name, void *target, size_t target_size)
{
    dlerror();
    void *symbol = dlsym(RTLD_DEFAULT, name);
    const char *error = dlerror();

    if (error != NULL || symbol == NULL || target_size != sizeof(symbol)) {
        fprintf(stderr, "gloader: Terraria's embedded Mono is missing required symbol %s\n", name);
        return 0;
    }

    memcpy(target, &symbol, sizeof(symbol));
    return 1;
}

static int load_mono_api(void)
{
    memset(&api, 0, sizeof(api));
    return
        load_symbol("mono_profiler_create", &api.profiler_create, sizeof(api.profiler_create)) &&
        load_symbol("mono_profiler_set_assembly_loaded_callback", &api.set_assembly_loaded_callback, sizeof(api.set_assembly_loaded_callback)) &&
        load_symbol("mono_get_root_domain", &api.get_root_domain, sizeof(api.get_root_domain)) &&
        load_symbol("mono_domain_assembly_open", &api.domain_assembly_open, sizeof(api.domain_assembly_open)) &&
        load_symbol("mono_assembly_get_name", &api.assembly_get_name, sizeof(api.assembly_get_name)) &&
        load_symbol("mono_assembly_name_get_name", &api.assembly_name_get_name, sizeof(api.assembly_name_get_name)) &&
        load_symbol("mono_assembly_get_image", &api.assembly_get_image, sizeof(api.assembly_get_image)) &&
        load_symbol("mono_class_from_name", &api.class_from_name, sizeof(api.class_from_name)) &&
        load_symbol("mono_class_get_method_from_name", &api.class_get_method_from_name, sizeof(api.class_get_method_from_name)) &&
        load_symbol("mono_runtime_invoke", &api.runtime_invoke, sizeof(api.runtime_invoke)) &&
        load_symbol("mono_object_unbox", &api.object_unbox, sizeof(api.object_unbox)) &&
        load_symbol("mono_object_to_string", &api.object_to_string, sizeof(api.object_to_string)) &&
        load_symbol("mono_string_to_utf8", &api.string_to_utf8, sizeof(api.string_to_utf8)) &&
        load_symbol("mono_free", &api.free_memory, sizeof(api.free_memory)) &&
        load_symbol("mono_set_assemblies_path", &api.set_assemblies_path, sizeof(api.set_assemblies_path));
}

static void fatal(const char *message)
{
    fprintf(stderr, "gloader: %s\n", message);
    fflush(stderr);
    _exit(1);
}

static void print_managed_exception(MonoObject *exception)
{
    if (exception == NULL)
        return;

    MonoObject *format_exception = NULL;
    MonoString *text = api.object_to_string(exception, &format_exception);
    if (text == NULL || format_exception != NULL) {
        fprintf(stderr, "gloader: managed initializer threw an exception (formatting failed)\n");
        return;
    }

    char *utf8 = api.string_to_utf8(text);
    if (utf8 == NULL) {
        fprintf(stderr, "gloader: managed initializer threw an exception\n");
        return;
    }

    fprintf(stderr, "gloader managed exception:\n%s\n", utf8);
    api.free_memory(utf8);
}

static const char *assembly_simple_name(MonoAssembly *assembly)
{
    if (assembly == NULL)
        return NULL;

    MonoAssemblyName *name = api.assembly_get_name(assembly);
    if (name == NULL)
        return NULL;

    return api.assembly_name_get_name(name);
}

static int is_attach_trigger(MonoAssembly *assembly)
{
    const char *simple_name = assembly_simple_name(assembly);
    if (simple_name == NULL)
        return 0;

    const char *mode = getenv("GLOADER_MODE");
    if (mode != NULL && strcmp(mode, "server") == 0)
        return strcmp(simple_name, "TerrariaServer") == 0;

    /*
     * The client embeds ReLogic and installs its own resolver during startup.
     * Waiting for ReLogic proves that Terraria's bootstrap has resolved its
     * private managed libraries before GLoader reflects over game types.
     */
    return strcmp(simple_name, "ReLogic") == 0;
}

static void attach_loader(void)
{
    const char *root = getenv("GLOADER_ROOT");
    if (root == NULL || *root == '\0')
        fatal("GLOADER_ROOT is missing while attaching to Terraria");

    char dependencies[PATH_MAX];
    char managed_loader[PATH_MAX];
    if (!join_path(dependencies, sizeof(dependencies), root, "gdeps") ||
        !join_path(managed_loader, sizeof(managed_loader), dependencies, "GLoader.dll")) {
        fatal("managed loader path is too long");
    }

    size_t search_length = strlen(root) + strlen(dependencies) + 2;
    char *search_path = (char *)malloc(search_length);
    if (search_path == NULL)
        fatal("out of memory while preparing Mono assembly search path");

    snprintf(search_path, search_length, "%s:%s", root, dependencies);
    api.set_assemblies_path(search_path);
    free(search_path);

    MonoDomain *domain = api.get_root_domain();
    if (domain == NULL)
        fatal("Terraria's embedded Mono root domain is unavailable");

    MonoAssembly *assembly = api.domain_assembly_open(domain, managed_loader);
    if (assembly == NULL)
        fatal("could not load gdeps/GLoader.dll inside Terraria's Mono runtime");

    MonoImage *image = api.assembly_get_image(assembly);
    MonoClass *entry = api.class_from_name(image, "GLoader", "Entry");
    if (entry == NULL)
        fatal("GLoader.Entry was not found in gdeps/GLoader.dll");

    MonoMethod *initialize = api.class_get_method_from_name(entry, "Initialize", 0);
    if (initialize == NULL)
        fatal("GLoader.Entry.Initialize() was not found");

    MonoObject *exception = NULL;
    MonoObject *result = api.runtime_invoke(initialize, NULL, NULL, &exception);
    if (exception != NULL) {
        print_managed_exception(exception);
        fflush(stderr);
        _exit(1);
    }

    if (result == NULL)
        fatal("managed initializer returned no status");

    const int exit_code = *(int *)api.object_unbox(result);
    if (exit_code != 0) {
        fprintf(stderr, "gloader: managed initializer failed with status %d\n", exit_code);
        fflush(stderr);
        _exit(exit_code > 0 && exit_code < 256 ? exit_code : 1);
    }
}

static void on_assembly_loaded(MonoProfiler *profiler, MonoAssembly *assembly)
{
    (void)profiler;

    if (initialized || initializing || !is_attach_trigger(assembly))
        return;

    initializing = 1;
    attach_loader();
    initialized = 1;
    initializing = 0;
}

__attribute__((visibility("default")))
void mono_profiler_init_gloader(const char *description)
{
    (void)description;

    if (!load_mono_api())
        fatal("could not resolve the profiler API from Terraria's embedded Mono runtime");

    MonoProfilerHandle handle = api.profiler_create(NULL);
    if (handle == NULL)
        fatal("Terraria's embedded Mono rejected the gloader profiler");

    api.set_assembly_loaded_callback(handle, on_assembly_loaded);
}
