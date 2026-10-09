"""Catch the XAML mistakes the compiler lets through.

The VB and XAML compilers do not resolve `{StaticResource ...}` keys or
`{Binding ...}` paths. A key that does not exist throws when the page is loaded
-- so it reaches the device -- and a typo in a binding path silently renders an
empty control. Both are cheap to check statically, and this script does exactly
that:

* every `StaticResource` key must be defined in App.xaml      -> failure
* every `Binding` path should exist on a known bindable type  -> warning

`ThemeResource` keys come from the OS theme (or the app dictionary) and cannot be
checked here, so they are only listed.

Usage:
    python tools/xaml_check.py
"""
import os
import re
import sys

APP_DIR = os.path.join("Proton VPN WP", "Proton VPN WP")
RESOURCE_OWNER = os.path.join(APP_DIR, "App.xaml")

KEY_DEFINITION = re.compile(r'x:Key="([^"]+)"')
RESOURCE_USE = re.compile(r'\{(StaticResource|ThemeResource)\s+([^}]+?)\s*\}')
BINDING_USE = re.compile(r'\{Binding\s+([^},]+)')
PROPERTY_DEFINITION = re.compile(
    r'^\s*(?:Public|Friend)\s+(?:ReadOnly\s+|Shadows\s+)*(?:Property|Enum)\s+(\w+)',
    re.MULTILINE)

# Everything a page can bind to: the model types used inside DataTemplates, and
# the view models that pages set as their DataContext.
BINDABLE_SOURCES = [
    "Models/ProtonLogical.vb",
    "Models/ProfileFieldRow.vb",
]
BINDABLE_DIRECTORIES = ["ViewModels"]

# Property names that exist on the generated/enum types used in templates.
EXTRA_BINDABLE = set()


def read(path):
    return open(path, encoding="utf-8-sig").read()


def xaml_files():
    yield RESOURCE_OWNER
    yield os.path.join(APP_DIR, "MainPage.xaml")
    views = os.path.join(APP_DIR, "Views")
    for name in sorted(os.listdir(views)):
        if name.endswith(".xaml"):
            yield os.path.join(views, name)


def collect_keys(text):
    return set(KEY_DEFINITION.findall(text))


def collect_properties():
    names = set()
    for relative in BINDABLE_SOURCES:
        path = os.path.join(APP_DIR, relative)
        if os.path.isfile(path):
            names |= set(PROPERTY_DEFINITION.findall(read(path)))
    for folder in BINDABLE_DIRECTORIES:
        directory = os.path.join(APP_DIR, folder)
        for name in sorted(os.listdir(directory)):
            if name.endswith(".vb"):
                names |= set(PROPERTY_DEFINITION.findall(read(os.path.join(directory, name))))
    return names | EXTRA_BINDABLE


def main():
    try:
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    except Exception:
        pass

    app_text = read(RESOURCE_OWNER)
    defined = collect_keys(app_text)
    bindable = collect_properties()

    if not defined:
        print("No resource keys found in {} -- the check would be vacuous."
              .format(RESOURCE_OWNER), file=sys.stderr)
        return 2

    print("App.xaml defines {} resource keys".format(len(defined)))
    print("Found {} bindable properties across the models and view models"
          .format(len(bindable)))
    print()

    missing_keys, unresolved_bindings, theme_keys = [], [], set()
    files_checked = 0

    for path in xaml_files():
        text = read(path)
        files_checked += 1
        where = os.path.relpath(path, APP_DIR)

        for kind, key in RESOURCE_USE.findall(text):
            if kind == "ThemeResource":
                theme_keys.add(key)
            elif key not in defined:
                missing_keys.append((where, key))

        for name in BINDING_USE.findall(text):
            name = name.strip()
            # Binding paths may be nested ("Servers.Count"); check the root only.
            root = name.split(".")[0]
            if root and root not in bindable:
                unresolved_bindings.append((where, name))

    print("Checked {} XAML files".format(files_checked))

    for where, key in missing_keys:
        print("  MISSING RESOURCE  {}: {{{{{{StaticResource {}}}}}}}".format(where, key))
    for where, name in unresolved_bindings:
        print("  UNRESOLVED BINDING {}: {{Binding {}}}".format(where, name))
    if theme_keys:
        print("  theme resources not checked (provided by the OS): {}"
              .format(", ".join(sorted(theme_keys))))

    if missing_keys:
        print("\nXAML check FAILED: {} resource key(s) are used but never defined."
              .format(len(missing_keys)))
        return 1

    if unresolved_bindings:
        print("\nXAML check passed with {} binding warning(s)."
              .format(len(unresolved_bindings)))
        return 0

    print("\nXAML check OK: every StaticResource key resolves and every binding "
          "path exists.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
