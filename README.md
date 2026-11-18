# USB-helper hardware database

This branch holds the hardware database of [USB-helper](https://github.com/klangche/USB-helper): the USB facts, the
operating systems and chips, the philosophies (guides) of USB-Helper and the ProAV manufacturers, and the computers with
known issues. It is the only source: every build of the app takes its files from the head of this branch, and the app
can download newer files from here without a new app version (#288).

```
v3/                  the files for apps that read database format 3 (schema_version in base.yaml)
├── base.yaml        facts from the USB specification
├── platforms/       one file per operating system, with its chips
├── guides/          one file per philosophy: what it counts (chain, tiers, hubs) and its limits
└── computers/       one file per machine with known issues
```

## Changing a file

Open a pull request to this branch. Its checks load the files with the app code of `dev` (and of the last release when
that reads the same format) and run the tests, so a file the app cannot read never lands. How the files are written:
[docs/database.md](https://github.com/klangche/USB-helper/blob/dev/docs/database.md).

## A new format

When the app's database format changes, a new folder (`v4/`) is added here first, next to `v3/`. `v3/` stays and keeps
serving the apps that read format 3.
