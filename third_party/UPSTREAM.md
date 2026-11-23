# Third-party provenance

This file records the known origin of the runtime payload currently embedded
in `EngineAssets`. Upstream source snapshots are under `sources/`. Mertviy does
not claim to have rebuilt the bundled binaries.

| Component | Local files | Known upstream | Version / revision | License information |
| --- | --- | --- | --- | --- |
| Flowseal zapret-discord-youtube runtime | `EngineAssets/winws.exe` and profile payloads | https://github.com/Flowseal/zapret-discord-youtube | Tag `1.9.9d`, commit `d75903b44cb7c8e7eedb998c3dd9ff674d10880a`; release hash list matches the files copied to `EngineAssets`. The exact bol-van commit used to build `winws.exe` is not identified. | MIT; notice in `EngineAssets/LICENSE.txt`; tagged source and `sources/Flowseal-1.9.9d-SHA256SUMS.txt` are included |
| WinDivert | `EngineAssets/WinDivert.dll`, `EngineAssets/WinDivert64.sys` | https://github.com/basil00/WinDivert | Binary metadata reports `2.2`; source tag `v2.2.2`, commit `1789526ecfb9ff5397c94f9f54c1a3dc2fb60440`, is included as the corresponding upstream source snapshot. | Dual licensed LGPL-3.0 or GPL-2.0; full text and source in `sources/WinDivert-2.2.2` |
| Cygwin runtime | `EngineAssets/cygwin1.dll` | https://github.com/cygwin/cygwin | Binary metadata `3.4.10`; source tag `cygwin-3.4.10`, commit `89a1e4a573d40b233011402f42630b970ff71291`, is included. | LGPL-3.0-or-later with Cygwin linking exception; source and license texts in `sources/Cygwin-3.4.10` |

The Flowseal `1.9.9d` source tree includes its original upstream `bin` files;
their release checksum list is preserved separately. Mertviy's own `EngineAssets`
are unchanged copies of those checksummed files. The app does not modify those
third-party binaries.

The packet-engine executable is checksum-verified as part of Flowseal 1.9.9d,
but its exact bol-van build commit is not established. The included bol-van
repository clones at other revisions are not asserted to match it. This is a
provenance limitation, not a claim that the binary was rebuilt from those
sources.
