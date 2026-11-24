# Enums reference

## What this is

Every enum of SystemX.Auth2: the values, what each one means, where the enum is used, which value to choose and why. The values and their short meanings are taken from the XML comments of the code (the generator is `tools/docs/enums.py`); the guidance next to them is written by hand.

## When to use it

When a setting or an option offers a list of values and you must pick one, or when you read a value in the database or an answer and want to know what it means.

## How to use it

- In **JSON** (settings, admin API) enum values are written as their names, for example `"mode": "Required"`. Numbers are not accepted in settings.
- In **configuration** (`appsettings.json`, environment variables) use the names too: `Auth2:Mode = SingleProject`, `Auth2__Messaging__Email__Security=StartTls`.
- In **C#** use the type: `PermissionSyncPolicy.Push`. The Contracts enums live in `SystemX.Auth2.Contracts.Enums`.
- Where a value is **stored** (a column of `projects.settings_json`, `external_group_mappings`, `permissions.source`), the same name is stored.
