#define _GNU_SOURCE

#include <errno.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include <unistd.h>

#ifndef PATH_MAX
#define PATH_MAX 4096
#endif

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

static const char *base_name(const char *path)
{
    const char *slash = strrchr(path, '/');
    return slash == NULL ? path : slash + 1;
}

static int is_client_command(const char *value)
{
    const char *name = base_name(value);
    return strcmp(name, "Terraria") == 0 || strcmp(name, "Terraria.bin.x86_64") == 0;
}

static int is_server_command(const char *value)
{
    const char *name = base_name(value);
    return strcmp(name, "TerrariaServer") == 0 || strcmp(name, "TerrariaServer.bin.x86_64") == 0;
}

static int is_terraria_command(const char *value)
{
    return value != NULL && (is_client_command(value) || is_server_command(value));
}

static void print_help(void)
{
    puts("gloader - native Linux/Mono Terraria source-mod loader");
    puts("");
    puts("Steam launch option:");
    puts("  ./gloader %command%");
    puts("");
    puts("Options:");
    puts("  --vanilla, --no-mods   Launch Terraria without injecting gloader");
    puts("  --server               Launch TerrariaServer through gloader");
    puts("  --help, -h             Show this help");
    puts("  --                     Pass all remaining arguments to Terraria");
}

static int is_gloader_profile(const char *token, size_t length)
{
    static const char prefix[] = "--profile=gloader";
    const size_t prefix_length = sizeof(prefix) - 1;

    if (length < prefix_length || strncmp(token, prefix, prefix_length) != 0)
        return 0;

    return length == prefix_length || token[prefix_length] == ':';
}

static char *build_mono_options(int inject_gloader)
{
    const char *existing = getenv("MONO_BUNDLED_OPTIONS");
    const size_t existing_length = existing == NULL ? 0 : strlen(existing);
    static const char profile[] = "--profile=gloader";
    const size_t capacity = existing_length + sizeof(profile) + 2;
    char *result = (char *)calloc(capacity, 1);

    if (result == NULL)
        return NULL;

    size_t written = 0;
    const char *cursor = existing;

    while (cursor != NULL && *cursor != '\0') {
        while (*cursor == ' ')
            cursor++;
        if (*cursor == '\0')
            break;

        const char *end = strchr(cursor, ' ');
        if (end == NULL)
            end = cursor + strlen(cursor);

        const size_t length = (size_t)(end - cursor);
        if (length > 0 && !is_gloader_profile(cursor, length)) {
            if (written > 0)
                result[written++] = ' ';
            memcpy(result + written, cursor, length);
            written += length;
            result[written] = '\0';
        }

        cursor = *end == '\0' ? end : end + 1;
    }

    if (inject_gloader) {
        if (written > 0)
            result[written++] = ' ';
        memcpy(result + written, profile, sizeof(profile));
    }

    return result;
}

static int prepend_library_path(const char *root)
{
    char dependencies[PATH_MAX];
    if (!join_path(dependencies, sizeof(dependencies), root, "gdeps"))
        return 0;

    const char *existing = getenv("LD_LIBRARY_PATH");
    const size_t length = strlen(dependencies) +
        ((existing != NULL && *existing != '\0') ? strlen(existing) + 2 : 1);
    char *value = (char *)malloc(length);
    if (value == NULL)
        return 0;

    if (existing != NULL && *existing != '\0')
        snprintf(value, length, "%s:%s", dependencies, existing);
    else
        snprintf(value, length, "%s", dependencies);

    const int ok = setenv("LD_LIBRARY_PATH", value, 1) == 0;
    free(value);
    return ok;
}

static int set_loader_environment(const char *root, int dedicated_server, int disable_mods)
{
    char *mono_options = build_mono_options(!disable_mods);
    if (mono_options == NULL)
        return 0;

    int ok = setenv("MONO_IOMAP", "all", 1) == 0;

    if (disable_mods) {
        if (ok)
            ok = unsetenv("GLOADER_ROOT") == 0;
        if (ok)
            ok = unsetenv("GLOADER_MODE") == 0;
        if (ok)
            ok = unsetenv("GLOADER_DISABLE_MODS") == 0;
    } else {
        if (ok)
            ok = setenv("GLOADER_ROOT", root, 1) == 0;
        if (ok)
            ok = setenv("GLOADER_MODE", dedicated_server ? "server" : "client", 1) == 0;
        if (ok)
            ok = setenv("GLOADER_DISABLE_MODS", "0", 1) == 0;
        if (ok)
            ok = prepend_library_path(root);
    }

    if (ok) {
        if (*mono_options != '\0')
            ok = setenv("MONO_BUNDLED_OPTIONS", mono_options, 1) == 0;
        else
            ok = unsetenv("MONO_BUNDLED_OPTIONS") == 0;
    }

    free(mono_options);
    return ok;
}

static int resolve_command_path(
    char *out,
    size_t size,
    const char *root,
    int dedicated_server)
{
    const char *binary = dedicated_server
        ? "TerrariaServer.bin.x86_64"
        : "Terraria.bin.x86_64";

    if (!join_path(out, size, root, binary))
        return 0;

    if (!is_file(out)) {
        fprintf(stderr, "gloader: Terraria MonoKickstart host not found: %s\n", out);
        return 0;
    }

    return 1;
}

static int handoff_steam_wrapper(
    const char *root,
    int argc,
    char **argv,
    int index,
    int disable_mods,
    int dedicated_server)
{
    if (index >= argc || is_terraria_command(argv[index]))
        return 0;

    int target_index = -1;
    for (int scan = index + 1; scan < argc; scan++) {
        if (is_terraria_command(argv[scan]))
            target_index = scan;
    }

    if (target_index < 0)
        return 0;

    char self[PATH_MAX];
    if (!join_path(self, sizeof(self), root, "gloader")) {
        fprintf(stderr, "gloader: executable path is too long\n");
        return -1;
    }

    const int option_count = (disable_mods ? 1 : 0) + (dedicated_server ? 1 : 0);
    const int original_count = argc - index;
    char **wrapper_argv = (char **)calloc(
        (size_t)original_count + (size_t)option_count + 2,
        sizeof(char *));

    if (wrapper_argv == NULL) {
        fprintf(stderr, "gloader: out of memory while preparing Steam wrapper handoff\n");
        return -1;
    }

    int out = 0;
    for (int source = index; source < argc; source++) {
        if (source == target_index) {
            wrapper_argv[out++] = self;

            if (disable_mods)
                wrapper_argv[out++] = "--vanilla";
            if (dedicated_server)
                wrapper_argv[out++] = "--server";

            wrapper_argv[out++] = argv[source];
        } else {
            wrapper_argv[out++] = argv[source];
        }
    }
    wrapper_argv[out] = NULL;

    execv(wrapper_argv[0], wrapper_argv);

    fprintf(
        stderr,
        "gloader: could not launch Steam wrapper %s: %s\n",
        wrapper_argv[0],
        strerror(errno));

    free(wrapper_argv);
    return -1;
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

    int disable_mods = 0;
    int dedicated_server = 0;
    int show_help = 0;
    int passthrough = 0;
    int index = 1;

    while (index < argc) {
        const char *argument = argv[index];

        if (strcmp(argument, "--") == 0) {
            passthrough = 1;
            index++;
            break;
        }
        if (strcmp(argument, "--vanilla") == 0 || strcmp(argument, "--no-mods") == 0) {
            disable_mods = 1;
            index++;
            continue;
        }
        if (strcmp(argument, "--server") == 0) {
            dedicated_server = 1;
            index++;
            continue;
        }
        if (strcmp(argument, "--help") == 0 || strcmp(argument, "-h") == 0) {
            show_help = 1;
            index++;
            continue;
        }
        break;
    }

    if (show_help) {
        print_help();
        return 0;
    }

    if (!passthrough) {
        const int wrapper_handoff = handoff_steam_wrapper(
            root,
            argc,
            argv,
            index,
            disable_mods,
            dedicated_server);

        if (wrapper_handoff < 0)
            return 1;
    }

    const char *requested_command = NULL;
    if (!passthrough && index < argc && is_terraria_command(argv[index])) {
        requested_command = argv[index++];
        if (is_server_command(requested_command))
            dedicated_server = 1;
    }

    char command[PATH_MAX];
    if (!resolve_command_path(command, sizeof(command), root, dedicated_server))
        return 1;

    if (!set_loader_environment(root, dedicated_server, disable_mods)) {
        fprintf(stderr, "gloader: could not prepare Terraria launch environment\n");
        return 1;
    }

    const int game_argc = argc - index;
    char **child_argv = (char **)calloc((size_t)game_argc + 2, sizeof(char *));
    if (child_argv == NULL) {
        fprintf(stderr, "gloader: out of memory while preparing Terraria arguments\n");
        return 1;
    }

    child_argv[0] = command;
    for (int game_index = 0; game_index < game_argc; game_index++)
        child_argv[game_index + 1] = argv[index + game_index];
    child_argv[game_argc + 1] = NULL;

    execv(command, child_argv);
    fprintf(stderr, "gloader: could not launch %s: %s\n", command, strerror(errno));
    free(child_argv);
    return 1;
}
