# DropSort third-party notices

DropSort V1 currently has no explicit project license. This file records third-party components
used by the Windows portable build; it does not grant a license to DropSort itself.

## PySide6 / Qt for Python / Shiboken6

The runtime uses PySide6 and Shiboken6 from Qt for Python. The installed distribution declares
`LGPL-3.0-only OR GPL-2.0-only OR GPL-3.0-only` (with commercial licensing also available from Qt).
Distribution must comply with the applicable Qt for Python and Qt license terms.

- https://doc.qt.io/qtforpython-6/licenses.html
- https://www.qt.io/licensing/open-source-lgpl-obligations

## Python

The packaged CPython runtime is distributed under the Python Software Foundation License Version 2.

- https://docs.python.org/3/license.html

## SQLite

SQLite states that its source code is in the public domain.

- https://www.sqlite.org/copyright.html

## PyInstaller

PyInstaller is a packaging/build dependency. Its bootloader exception permits distributing bundled
applications under the application's chosen terms, subject to PyInstaller's license conditions.

- https://pyinstaller.org/en/stable/license.html

## Inter font

Inter is distributed under the SIL Open Font License 1.1. DropSort bundles the official Regular and
Bold assets for deterministic offline Latin UI rendering. The complete `Inter-OFL.txt` is bundled
next to the fonts in the release.

- https://github.com/rsms/inter

## Noto Sans Arabic font

Inter does not include Arabic glyph coverage. DropSort therefore bundles Noto Sans Arabic Regular
and Bold as its deterministic offline Arabic companion. Noto Sans Arabic is distributed under the
SIL Open Font License 1.1; `NotoSansArabic-OFL.txt` is bundled next to the fonts in the release.

- https://github.com/notofonts/arabic

## The Movie Database (TMDB)

Movie metadata and poster images are supplied through TMDB when configured. TMDB attribution and
the required non-endorsement notice appear in DropSort's Settings > About & Credits section.

- https://developer.themoviedb.org/docs/faq
- https://www.themoviedb.org/about/logos-attribution
- https://www.themoviedb.org/api-terms-of-use

## Microsoft Fluent UI System Icons

DropSort vendors the minimum official 16px Filled SVG assets from Microsoft's
Fluent UI System Icons project for the desktop icon system. The SVG paths are
official Fluent assets and their monochrome color token is adapted to
`currentColor` so Qt can resolve the icon through DropSort's theme palette.

- https://github.com/microsoft/fluentui-system-icons
- `licenses/Microsoft-Fluent-UI-System-Icons-LICENSE.txt`
- `licenses/Microsoft-Fluent-UI-System-Icons-NOTICE.txt`
