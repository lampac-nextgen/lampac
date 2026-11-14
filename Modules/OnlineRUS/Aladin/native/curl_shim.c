/*
 * Non-variadic C shim for libcurl's variadic curl_easy_setopt().
 *
 * A managed P/Invoke with a fixed signature cannot safely call a variadic C
 * function on every architecture (notably arm64).  These wrappers take the
 * real curl_easy_setopt() function pointer from the loaded libcurl and call it
 * with the correctly typed argument.
 */

#include <stdint.h>

typedef int (*curl_setopt_fn)(void *easy, int option, ...);

int aladin_curl_setopt_long(void *fn, void *easy, int option, int64_t value)
{
    return ((curl_setopt_fn)fn)(easy, option, (long)value);
}

int aladin_curl_setopt_ptr(void *fn, void *easy, int option, void *value)
{
    return ((curl_setopt_fn)fn)(easy, option, value);
}

int aladin_curl_setopt_string(void *fn, void *easy, int option, const char *value)
{
    return ((curl_setopt_fn)fn)(easy, option, value);
}
