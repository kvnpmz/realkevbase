"""Resolve Azure Storage credentials for the maintenance scripts.

The storage connection string is a secret and lives in Azure App Service settings.
These helpers read it at run time so no script, shell profile or repository file ever
holds a copy: it is fetched into memory, used for one az call, and discarded.

Resolution order:
  1. ``AZURE_STORAGE_CONNECTION_STRING`` when the caller has already exported one
  2. the live App Service setting (requires ``az login`` with permission to read it)
  3. ``az login`` identity, which needs the *Storage Blob Data Contributor* role

Nothing here ever prints the secret.
"""

from __future__ import annotations

import os
import subprocess

DEFAULT_RESOURCE_GROUP = "realkevbase-rg"
DEFAULT_APP_NAME = "realkevbase-platform"
DEFAULT_ACCOUNT_NAME = "realkevbasestorage"
SETTING_NAME = "ConnectionStrings__AzureStorage"

#: Describes how credentials were obtained, for the scripts to report.
SOURCE_ENV = "AZURE_STORAGE_CONNECTION_STRING"
SOURCE_APP_SETTINGS = "app settings"
SOURCE_LOGIN = "az login identity"


def resolve_connection_string() -> tuple[str | None, str]:
    """Find a storage connection string.

    Returns:
        A tuple of the connection string (or ``None``) and a short label describing
        where it came from, for logging.
    """
    exported = os.environ.get("AZURE_STORAGE_CONNECTION_STRING")
    if exported:
        return exported, SOURCE_ENV

    resource_group = os.environ.get("AZURE_RESOURCE_GROUP", DEFAULT_RESOURCE_GROUP)
    app_name = os.environ.get("AZURE_APP_NAME", DEFAULT_APP_NAME)

    command = [
        "az", "webapp", "config", "appsettings", "list",
        "--resource-group", resource_group,
        "--name", app_name,
        "--query", f"[?name=='{SETTING_NAME}'].value",
        "--output", "tsv",
    ]

    try:
        result = subprocess.run(command, capture_output=True, text=True, timeout=120)
    except (OSError, subprocess.SubprocessError):
        return None, ""

    if result.returncode != 0:
        return None, ""

    value = result.stdout.strip()
    return (value, f"{SOURCE_APP_SETTINGS} ({app_name})") if value else (None, "")


def storage_arguments() -> tuple[list[str], str]:
    """Build the authentication arguments for an az storage command.

    Returns:
        A tuple of the argument list to append to an ``az storage`` command and a
        label describing the credential source.
    """
    connection_string, source = resolve_connection_string()
    if connection_string:
        # Appended, never spliced in: an argument placed mid-command makes the CLI
        # read the value as a subcommand name.
        return ["--connection-string", connection_string], source

    account = os.environ.get("AZURE_STORAGE_ACCOUNT", DEFAULT_ACCOUNT_NAME)
    return ["--auth-mode", "login", "--account-name", account], SOURCE_LOGIN


def connection_string_or_none() -> str | None:
    """Return the connection string when one can be resolved, ignoring the source."""
    return resolve_connection_string()[0]
