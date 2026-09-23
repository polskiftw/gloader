#define _GNU_SOURCE

#include <dirent.h>
#include <dlfcn.h>
#include <errno.h>
#include <limits.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

#ifndef PATH_MAX
#define PATH_MAX 4096
#endif

typedef struct _MonoDomain MonoDomain;
typedef struct _MonoAssembly MonoAssembly;
typedef struct _MonoImage MonoImage;
typedef struct _MonoClass MonoClass;
typedef struct _MonoMethod MonoMethod;
typedef struct _MonoObject MonoObject;
typedef struct _MonoString MonoString;

typedef void (*mono_config_parse_fn)(const char *);
typedef void (*mono_set_dirs_fn)(const char *, const char *);
typedef MonoDomain *(*mono_jit_init_version_fn)(const char *, const char *);
typedef MonoAssembly *(*mono_domain_assembly_open_fn)(MonoDomain *, const char *);
typedef MonoImage *(*mono_assembly_get_image_fn)(MonoAssembly *);
typedef MonoClass *(*mono_class_from_name_fn)(MonoImage *, const char *, const char *);
typedef MonoMethod *(*mono_class_get_method_from_name_fn)(MonoClass *, const char *, int);
typedef MonoObject *(*mono_runtime_invoke_fn)(MonoMethod *, void *, void **, MonoObject **);
typedef void *(*mono_object_unbox_fn)(MonoObject *);
typedef MonoString *(*mono_object_to_string_fn)(MonoObject *, MonoObject **);
typedef char *(*mono_string_to_utf8_fn)(MonoString *);
typedef void (*mono_free_fn)(void *);
typedef void (*mono_jit_cleanup_fn)(MonoDomain *);

struct mono_api {
    mono_config_parse_fn config_parse;
    mono_set_dirs_fn set_dirs;
    mono_jit_init_version_fn jit_init_version;
    mono_domain_assembly_open_fn domain_assembly_open;
    mono_assembly_get_image_fn assembly_get_image;
    mono_class_from_name_fn class_from_name;
    mono_class_get_method_from_name_fn class_get_method_from_name;
    mono_runtime_invoke_fn runtime_invoke;
    mono_object_unbox_fn object_unbox;
    mono_object_to_string_fn object_to_string;
    mono_string_to_utf8_fn string_to_utf8;
    mono_free_fn free_memory;
    mono_jit_cleanup_fn jit_cleanup;
};

static int is_directory(const char *path)
{
    struct stat info;
    return stat(path, &info) == 0 && S_ISDIR(info.st_mode);
}

static int is_file(const char *path)
{
    struct stat info;
    return stat(path, &info) == 0 && S_ISREG(info.st_mode);
}

static int join_path(char *out, size_t size, const char *left, const char *right)
{
    const int written = snprintf(out, size, "%s/%s", left, right);
    return written >= 0 && (size_t)written < size;
}

static int get_executable_root(char *out, size_t size)
{
    char path[PATH_MAX];
    const ssize_t length = readlink("/proc/self/exe", path, sizeof(path) - 1);

    if (length < 0) {
        fprintf(stderr, "gloader: could not resolve /proc/self/exe: %s\n", strerror(errno));
        return 0;
    }

    path[length] = '\0';
    char *slash = strrchr(path, '/');
    if (slash == NULL) {
        fprintf(stderr, "gloader: executable path has no directory component\n");
        return 0;
    }

    *slash = '\0';
    if (snprintf(out, size, "%s", path) >= (int)size) {
        fprintf(stderr, "gloader: Terraria path is too long\n");
        return 0;
    }

    return 1;
}

static int looks_like_mono_library(const char *name)
{
    if (strncmp(name, "libmono", 7) != 0)
        return 0;

    return strstr(name, ".so") != NULL;
}

static int find_mono_library_recursive(
    const char *directory,
    char *out,
    size_t out_size,
    int depth)
{
    if (depth < 0)
        return 0;

    DIR *dir = opendir(directory);
    if (dir == NULL)
        return 0;

    struct dirent *entry;
    while ((entry = readdir(dir)) != NULL) {
        if (strcmp(entry->d_name, ".") == 0 || strcmp(entry->d_name, "..") == 0)
            continue;

        char candidate[PATH_MAX];
        if (!join_path(candidate, sizeof(candidate), directory, entry->d_name))
            continue;

        if (is_file(candidate) && looks_like_mono_library(entry->d_name)) {
            if (snprintf(out, out_size, "%s", candidate) < (int)out_size) {
                closedir(dir);
                return 1;
            }
        }

        if (depth > 0 && is_directory(candidate)) {
            if (find_mono_library_recursive(candidate, out, out_size, depth - 1)) {
                closedir(dir);
                return 1;
            }
        }
    }

    closedir(dir);
    return 0;
}

static void *open_bundled_mono(const char *root, char *selected, size_t selected_size)
{
    static const char *relative_candidates[] = {
        "lib64/libmonosgen-2.0.so.1",
        "lib64/libmonosgen-2.0.so",
        "lib64/libmono-2.0.so.1",
        "lib64/libmono-2.0.so",
        "lib/libmonosgen-2.0.so.1",
        "lib/libmonosgen-2.0.so",
        "lib/libmono-2.0.so.1",
        "lib/libmono-2.0.so"
    };

    for (size_t index = 0;
         index < sizeof(relative_candidates) / sizeof(relative_candidates[0]);
         index++) {
        char candidate[PATH_MAX];
        if (!join_path(candidate, sizeof(candidate), root, relative_candidates[index]))
            continue;

        if (!is_file(candidate))
            continue;

        void *handle = dlopen(candidate, RTLD_NOW | RTLD_GLOBAL);
        if (handle != NULL) {
            snprintf(selected, selected_size, "%s", candidate);
            return handle;
        }
    }

    const char *fallback_roots[] = { "lib64", "lib" };
    for (size_t index = 0; index < 2; index++) {
        char search_root[PATH_MAX];
        if (!join_path(search_root, sizeof(search_root), root, fallback_roots[index]))
            continue;

        char candidate[PATH_MAX];
        if (!find_mono_library_recursive(search_root, candidate, sizeof(candidate), 2))
            continue;

        void *handle = dlopen(candidate, RTLD_NOW | RTLD_GLOBAL);
        if (handle != NULL) {
            snprintf(selected, selected_size, "%s", candidate);
            return handle;
        }
    }

    fprintf(stderr,
        "gloader: could not load Terraria's bundled Mono runtime.\n"
        "Expected libmonosgen-2.0 or libmono-2.0 beneath %s/lib64 or %s/lib.\n",
        root,
        root);
    return NULL;
}

static int load_required_symbol(void *handle, const char *name, void *target, size_t target_size)
{
    dlerror();
    void *symbol = dlsym(handle, name);
    const char *error = dlerror();

    if (error != NULL || symbol == NULL) {
        fprintf(stderr, "gloader: bundled Mono is missing required symbol %s\n", name);
        return 0;
    }

    if (target_size != sizeof(symbol)) {
        fprintf(stderr, "gloader: unsupported function-pointer ABI for %s\n", name);
        return 0;
    }

    memcpy(target, &symbol, sizeof(symbol));
    return 1;
}

static void load_optional_symbol(void *handle, const char *name, void *target, size_t target_size)
{
    dlerror();
    void *symbol = dlsym(handle, name);
    if (dlerror() != NULL || symbol == NULL || target_size != sizeof(symbol))
        return;

    memcpy(target, &symbol, sizeof(symbol));
}

static int load_mono_api(void *handle, struct mono_api *api)
{
    memset(api, 0, sizeof(*api));

    if (!load_required_symbol(handle, "mono_config_parse", &api->config_parse, sizeof(api->config_parse)) ||
        !load_required_symbol(handle, "mono_jit_init_version", &api->jit_init_version, sizeof(api->jit_init_version)) ||
        !load_required_symbol(handle, "mono_domain_assembly_open", &api->domain_assembly_open, sizeof(api->domain_assembly_open)) ||
        !load_required_symbol(handle, "mono_assembly_get_image", &api->assembly_get_image, sizeof(api->assembly_get_image)) ||
        !load_required_symbol(handle, "mono_class_from_name", &api->class_from_name, sizeof(api->class_from_name)) ||
        !load_required_symbol(handle, "mono_class_get_method_from_name", &api->class_get_method_from_name, sizeof(api->class_get_method_from_name)) ||
        !load_required_symbol(handle, "mono_runtime_invoke", &api->runtime_invoke, sizeof(api->runtime_invoke)) ||
        !load_required_symbol(handle, "mono_object_unbox", &api->object_unbox, sizeof(api->object_unbox)) ||
        !load_required_symbol(handle, "mono_object_to_string", &api->object_to_string, sizeof(api->object_to_string)) ||
        !load_required_symbol(handle, "mono_string_to_utf8", &api->string_to_utf8, sizeof(api->string_to_utf8)) ||
        !load_required_symbol(handle, "mono_jit_cleanup", &api->jit_cleanup, sizeof(api->jit_cleanup))) {
        return 0;
    }

    load_optional_symbol(handle, "mono_set_dirs", &api->set_dirs, sizeof(api->set_dirs));
    load_optional_symbol(handle, "mono_free", &api->free_memory, sizeof(api->free_memory));
    return 1;
}

static char *encode_arguments(int argc, char **argv)
{
    size_t length = 0;

    for (int index = 1; index < argc; index++) {
        if (index > 1)
            length++;
        length += strlen(argv[index]) * 2;
    }

    char *encoded = (char *)malloc(length + 1);
    if (encoded == NULL)
        return NULL;

    static const char hex[] = "0123456789abcdef";
    char *write = encoded;

    for (int index = 1; index < argc; index++) {
        if (index > 1)
            *write++ = ',';

        const unsigned char *input = (const unsigned char *)argv[index];
        while (*input != '\0') {
            *write++ = hex[*input >> 4];
            *write++ = hex[*input & 0x0f];
            input++;
        }
    }

    *write = '\0';
    return encoded;
}

static void configure_mono_directories(const char *root, const struct mono_api *api)
{
    if (api->set_dirs == NULL)
        return;

    char candidate[PATH_MAX];
    char mono_dir[PATH_MAX];

    if (join_path(candidate, sizeof(candidate), root, "lib64") &&
        join_path(mono_dir, sizeof(mono_dir), candidate, "mono") &&
        is_directory(mono_dir)) {
        api->set_dirs(candidate, root);
        return;
    }

    if (join_path(candidate, sizeof(candidate), root, "lib") &&
        join_path(mono_dir, sizeof(mono_dir), candidate, "mono") &&
        is_directory(mono_dir)) {
        api->set_dirs(candidate, root);
    }
}

static int set_loader_environment(const char *root, int argc, char **argv)
{
    char dependencies[PATH_MAX];
    if (!join_path(dependencies, sizeof(dependencies), root, "gdeps"))
        return 0;

    const char *existing = getenv("MONO_PATH");
    size_t mono_path_length = strlen(root) + strlen(dependencies) + 2;
    if (existing != NULL && *existing != '\0')
        mono_path_length += strlen(existing) + 1;

    char *mono_path = (char *)malloc(mono_path_length);
    if (mono_path == NULL)
        return 0;

    if (existing != NULL && *existing != '\0')
        snprintf(mono_path, mono_path_length, "%s:%s:%s", root, dependencies, existing);
    else
        snprintf(mono_path, mono_path_length, "%s:%s", root, dependencies);

    char *encoded = encode_arguments(argc, argv);
    if (encoded == NULL) {
        free(mono_path);
        return 0;
    }

    const int ok =
        setenv("GLOADER_ROOT", root, 1) == 0 &&
        setenv("GLOADER_ARGV_HEX", encoded, 1) == 0 &&
        setenv("MONO_PATH", mono_path, 1) == 0;

    free(encoded);
    free(mono_path);
    return ok;
}

static void print_managed_exception(const struct mono_api *api, MonoObject *exception)
{
    if (exception == NULL)
        return;

    MonoObject *format_exception = NULL;
    MonoString *text = api->object_to_string(exception, &format_exception);

    if (text == NULL || format_exception != NULL) {
        fprintf(stderr, "gloader: managed loader threw an exception (formatting failed)\n");
        return;
    }

    char *utf8 = api->string_to_utf8(text);
    if (utf8 == NULL) {
        fprintf(stderr, "gloader: managed loader threw an exception\n");
        return;
    }

    fprintf(stderr, "gloader managed exception:\n%s\n", utf8);

    if (api->free_memory != NULL)
        api->free_memory(utf8);
}

int main(int argc, char **argv)
{
    char root[PATH_MAX];
    if (!get_executable_root(root, sizeof(root)))
        return 1;

    if (chdir(root) != 0) {
        fprintf(stderr, "gloader: could not enter Terraria directory %s: %s\n", root, strerror(errno));
        return 1;
    }

    if (!set_loader_environment(root, argc, argv)) {
        fprintf(stderr, "gloader: could not prepare loader environment\n");
        return 1;
    }

    char mono_library[PATH_MAX];
    void *mono_handle = open_bundled_mono(root, mono_library, sizeof(mono_library));
    if (mono_handle == NULL)
        return 1;

    struct mono_api api;
    if (!load_mono_api(mono_handle, &api))
        return 1;

    configure_mono_directories(root, &api);

    char mono_config[PATH_MAX];
    if (join_path(mono_config, sizeof(mono_config), root, "monoconfig") && is_file(mono_config))
        api.config_parse(mono_config);
    else
        api.config_parse(NULL);

    MonoDomain *domain = api.jit_init_version("gloader", "v4.0.30319");
    if (domain == NULL) {
        fprintf(stderr, "gloader: Terraria's bundled Mono runtime could not initialize\n");
        return 1;
    }

    char managed_loader[PATH_MAX];
    if (!join_path(managed_loader, sizeof(managed_loader), root, "gdeps/GLoader.dll")) {
        fprintf(stderr, "gloader: managed loader path is too long\n");
        api.jit_cleanup(domain);
        return 1;
    }

    MonoAssembly *assembly = api.domain_assembly_open(domain, managed_loader);
    if (assembly == NULL) {
        fprintf(stderr, "gloader: could not load %s\n", managed_loader);
        api.jit_cleanup(domain);
        return 1;
    }

    MonoImage *image = api.assembly_get_image(assembly);
    MonoClass *entry_class = api.class_from_name(image, "GLoader", "Entry");
    if (entry_class == NULL) {
        fprintf(stderr, "gloader: GLoader.Entry was not found in %s\n", managed_loader);
        api.jit_cleanup(domain);
        return 1;
    }

    MonoMethod *run = api.class_get_method_from_name(entry_class, "Run", 0);
    if (run == NULL) {
        fprintf(stderr, "gloader: GLoader.Entry.Run() was not found\n");
        api.jit_cleanup(domain);
        return 1;
    }

    MonoObject *exception = NULL;
    MonoObject *result = api.runtime_invoke(run, NULL, NULL, &exception);

    if (exception != NULL) {
        print_managed_exception(&api, exception);
        api.jit_cleanup(domain);
        return 1;
    }

    if (result == NULL) {
        fprintf(stderr, "gloader: managed loader returned no exit status\n");
        api.jit_cleanup(domain);
        return 1;
    }

    int exit_code = *(int *)api.object_unbox(result);
    api.jit_cleanup(domain);
    return exit_code;
}
