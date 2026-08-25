from __future__ import annotations

from pathlib import Path
from typing import NamedTuple

from PySide6.QtGui import QFontDatabase
from PySide6.QtWidgets import QApplication

from dropsort.application.configuration.theme import UiTheme

ThemeId = UiTheme


class ColorTokens(NamedTuple):
    text: str
    background: str
    primary: str
    secondary: str
    accent: str
    surface: str
    surface_raised: str
    card: str
    text_muted: str
    border: str
    danger: str
    card_hover: str
    text_secondary: str
    success: str
    warning: str
    primary_hover: str
    accent_hover: str
    sidebar: str
    selected: str
    focus: str
    disabled: str


DEEP_INK = ColorTokens(
    text="#FFF1A6",
    background="#0B1E1B",
    primary="#013C35",
    secondary="#6B352A",
    accent="#E87454",
    surface="#102925",
    surface_raised="#17332E",
    card="#102925",
    text_muted="#C9BE88",
    border="#285047",
    danger="#FF8A72",
    card_hover="#17332E",
    text_secondary="#E6DBA5",
    success="#6FCF97",
    warning="#D6A84B",
    primary_hover="#0A5148",
    accent_hover="#F08A6B",
    sidebar="#013C35",
    selected="#6B352A",
    focus="#F08A6B",
    disabled="#285047",
)

CHATGPT_DARK = ColorTokens(
    text="#F2F2F2", background="#1C1D1F", primary="#D9A441", secondary="#303338",
    accent="#C86A4A", surface="#232528", surface_raised="#2A2D31", card="#26282C",
    text_muted="#858A91", border="#3A3D42", danger="#D9786B", card_hover="#303338",
    text_secondary="#B9BCC1", success="#79B88A", warning="#D9A441",
    primary_hover="#E8B652", accent_hover="#F0A06F",
    sidebar="#232528", selected="#303338", focus="#E8B652", disabled="#3A3D42",
)

SLATE = ColorTokens(
    text="#E3E2E3", background="#18212B", primary="#B3C9DD", secondary="#2B3C4D",
    accent="#D97757", surface="#202C38", surface_raised="#273746", card="#243342",
    text_muted="#8D9196", border="#3C5164", danger="#D9534F", card_hover="#2B3C4D",
    text_secondary="#C3C7CC", success="#5CB85C", warning="#E6BF9E",
    primary_hover="#C3D6E7", accent_hover="#E58B6E",
    sidebar="#202C38", selected="#314656", focus="#B3C9DD", disabled="#3C5164",
)

LIGHT = ColorTokens(
    # Same geometry as the dark/Slate product, with Light-only semantic roles.
    # The previous green sidebar/primary was a dark-theme holdover that made
    # global text and muted roles inconsistent in Light mode.
    text="#26292D", background="#F4F1EA", primary="#526A7C", secondary="#E8EDF1",
    accent="#D97757", surface="#FBF9F4", surface_raised="#FFFFFF", card="#F8F6F1",
    text_muted="#6F747A", border="#D5D9DD", danger="#B84C48", card_hover="#EEF1F3",
    text_secondary="#525960", success="#3E7D5D", warning="#9B6B32",
    primary_hover="#40586A", accent_hover="#C9684B",
    sidebar="#EEEAE3", selected="#E3E8EC", focus="#526A7C", disabled="#DADDE0",
)

THEMES = {
    UiTheme.MAIN: DEEP_INK,
    UiTheme.DARK: CHATGPT_DARK,
    UiTheme.SLATE: SLATE,
    UiTheme.LIGHT: LIGHT,
}
COLORS = DEEP_INK

FONT_FAMILY = "Inter"
ARABIC_FONT_FAMILY = "Noto Sans Arabic"
FALLBACK_FONT_FAMILY = "Segoe UI"
BODY_WEIGHT = 400
HEADING_WEIGHT = 600
BODY_SIZE = 14
H1_SIZE = 28
H2_SIZE = 20
H3_SIZE = 20
H4_SIZE = 16
H5_SIZE = 14
SMALL_SIZE = 12
PAGE_TITLE_SIZE = 24
SCREEN_HEADING_SIZE = 24
SECTION_HEADING_SIZE = 16
MOVIE_TITLE_SIZE = 13
_FONT_ROOT = Path(__file__).parent.parent / "assets" / "fonts"
INTER_REGULAR_FONT_PATH = _FONT_ROOT / "Inter-Regular.otf"
INTER_BOLD_FONT_PATH = _FONT_ROOT / "Inter-Bold.otf"
NOTO_SANS_ARABIC_REGULAR_FONT_PATH = _FONT_ROOT / "NotoSansArabic-Regular.ttf"
NOTO_SANS_ARABIC_BOLD_FONT_PATH = _FONT_ROOT / "NotoSansArabic-Bold.ttf"
APPLICATION_FONT_PATHS = (
    INTER_REGULAR_FONT_PATH,
    INTER_BOLD_FONT_PATH,
    NOTO_SANS_ARABIC_REGULAR_FONT_PATH,
    NOTO_SANS_ARABIC_BOLD_FONT_PATH,
)
_FONT_REGISTRATION_RESULTS: dict[tuple[Path, ...], bool] = {}

CARD_WIDTH = 168
POSTER_HEIGHT = 252
CARD_HEIGHT = 296

# Shared WinUI-inspired geometry primitives. Existing aliases remain stable
# for the current views while new UI can use the explicit scale.
SPACE_4 = 4
SPACE_8 = 8
SPACE_12 = 12
SPACE_16 = 16
SPACE_24 = 24
SPACE_36 = 36
SPACE_48 = 48
SPACE_SMALL = SPACE_8
SPACE_MEDIUM = SPACE_16
SPACE_LARGE = SPACE_24
RADIUS_CONTROL = 4
RADIUS_OVERLAY = 8
RADIUS_SMALL = RADIUS_CONTROL
RADIUS_MEDIUM = RADIUS_OVERLAY
CONTROL_HEIGHT = 36
NAVIGATION_ITEM_HEIGHT = 42
# Compatibility value for integrations that still import the former compact
# pane metric. The active shell no longer implements a compact mode.
COMPACT_NAVIGATION_ITEM_WIDTH = 44
ICON_SIZE = 16
ICON_TEXT_GAP = SPACE_8


def register_application_fonts(
    font_paths: tuple[Path, ...] = APPLICATION_FONT_PATHS,
) -> bool:
    """Register deterministic Latin and Arabic font assets once."""

    paths = tuple(Path(path) for path in font_paths)
    cached = _FONT_REGISTRATION_RESULTS.get(paths)
    if cached is not None:
        return cached
    families: set[str] = set()
    for path in paths:
        font_id = QFontDatabase.addApplicationFont(str(path))
        if font_id < 0:
            _FONT_REGISTRATION_RESULTS[paths] = False
            return False
        families.update(QFontDatabase.applicationFontFamilies(font_id))
    if paths == APPLICATION_FONT_PATHS:
        registered = FONT_FAMILY in families and ARABIC_FONT_FAMILY in families
    else:
        registered = bool(families)
    _FONT_REGISTRATION_RESULTS[paths] = registered
    return registered


def application_stylesheet(theme_id: UiTheme | str = UiTheme.MAIN) -> str:
    try:
        selected = UiTheme(theme_id)
    except (TypeError, ValueError):
        selected = UiTheme.MAIN
    colors = THEMES[selected]
    font_stack = (
        f'"{FONT_FAMILY}", "{ARABIC_FONT_FAMILY}", "{FALLBACK_FONT_FAMILY}"'
    )
    return f"""
        * {{
            font-family: {font_stack};
            font-size: {BODY_SIZE:g}px;
            font-weight: {BODY_WEIGHT};
            color: {colors.text};
        }}
        QMainWindow, QWidget#appRoot, QWidget#settingsView, QWidget#libraryCheckPage {{
            background: {colors.background};
        }}
        QFrame#sidebar {{
            background: {colors.sidebar};
            border-right: 1px solid {colors.border};
        }}
        QFrame#sidebarFooter {{
            background: transparent;
            border: none;
        }}
        QLabel#brandLabel {{
            color: {colors.text};
            font-size: {H2_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#brandSubtitle, QLabel[role="muted"] {{
            color: {colors.text_muted};
        }}
        QLabel[role="h1"] {{ font-size: {H1_SIZE:g}px; font-weight: {HEADING_WEIGHT}; }}
        QLabel[role="screenHeading"] {{
            font-size: {SCREEN_HEADING_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel[role="h2"] {{ font-size: {H2_SIZE:g}px; font-weight: {HEADING_WEIGHT}; }}
        QLabel[role="h3"] {{ font-size: {H3_SIZE:g}px; font-weight: {HEADING_WEIGHT}; }}
        QLabel[role="heading"], QLabel[role="detailsHeading"] {{
            font-size: {PAGE_TITLE_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel[role="h4"] {{ font-size: {H4_SIZE:g}px; font-weight: {HEADING_WEIGHT}; }}
        QLabel[role="sectionHeading"] {{
            font-size: {SECTION_HEADING_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel[role="h5"] {{
            font-size: {H5_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel[role="small"] {{ font-size: {SMALL_SIZE:g}px; }}
        QLabel[role="primary"] {{ color: {colors.text}; }}
        QLabel[role="secondary"] {{ color: {colors.text_secondary}; }}
        QLabel[role="tertiary"], QLabel[role="muted"] {{ color: {colors.text_muted}; }}
        QLabel[role="error"] {{ color: {colors.danger}; }}
        QLabel[role="success"] {{ color: {colors.success}; }}
        QLabel[role="warning"] {{ color: {colors.warning}; }}
        QPushButton {{
            background: transparent;
            border: 1px solid transparent;
            border-radius: {RADIUS_CONTROL}px;
            min-height: {CONTROL_HEIGHT}px;
            padding: 8px 12px;
            text-align: left;
        }}
        QToolButton {{
            min-width: 28px;
            min-height: 28px;
            padding: 4px;
            border-radius: {RADIUS_CONTROL}px;
        }}
        QPushButton:hover {{
            background: {colors.surface_raised};
        }}
        QPushButton:pressed {{
            background: {colors.secondary};
        }}
        QPushButton:focus {{
            border: 1px solid {colors.focus};
        }}
        QPushButton:checked {{
            background: {colors.selected};
            border-left: 3px solid {colors.accent};
            color: {colors.text};
            font-weight: {HEADING_WEIGHT};
        }}
        QFrame#sidebar QPushButton[role="navigationItem"] {{
            min-height: {NAVIGATION_ITEM_HEIGHT}px;
            max-height: {NAVIGATION_ITEM_HEIGHT}px;
            padding: 0 12px;
            border: none;
            border-radius: {RADIUS_CONTROL}px;
            background: transparent;
            text-align: left;
            font-size: 13px;
            font-weight: {BODY_WEIGHT};
        }}
        QFrame#sidebar QPushButton[role="navigationItem"]:hover {{
            border: none;
            background: {colors.card_hover};
        }}
        QFrame#sidebar QPushButton[role="navigationItem"]:rtl {{
            text-align: right;
        }}
        QFrame#sidebar QPushButton[role="navigationItem"]:checked {{
            border: none;
            background: {colors.selected};
            color: {colors.text};
            font-weight: {BODY_WEIGHT};
        }}
        QFrame#navigationAccent {{
            min-width: 3px;
            max-width: 3px;
            min-height: 24px;
            max-height: 24px;
            border: none;
            border-radius: 1px;
            background: {colors.accent};
        }}
        QPushButton[role="secondaryAction"] {{
            background: {colors.surface_raised};
            border-color: {colors.border};
            color: {colors.text};
            text-align: center;
        }}
        QPushButton[role="secondaryAction"]:hover {{
            background: {colors.selected};
            border-color: {colors.focus};
        }}
        QPushButton[role="primaryAction"] {{
            background: {colors.selected};
            border-color: {colors.border};
            color: {colors.text};
            font-weight: {HEADING_WEIGHT};
            text-align: center;
        }}
        QPushButton[role="primaryAction"]:hover {{
            border-color: {colors.focus};
            background: {colors.card_hover};
            color: {colors.text};
        }}
        QPushButton[role="preferenceAction"] {{
            background: {colors.surface_raised};
            border-color: {colors.border};
            color: {colors.text};
            text-align: center;
        }}
        QPushButton[role="preferenceAction"]:hover {{
            background: {colors.selected};
            border-color: {colors.focus};
        }}
        QPushButton[role="preferenceAction"]:checked {{
            background: {colors.selected};
            border: 1px solid {colors.border};
            color: {colors.text};
            font-weight: {HEADING_WEIGHT};
        }}
        QPushButton[role="watchAction"] {{
            background: {colors.surface_raised};
            border-color: {colors.border};
            color: {colors.text};
            font-weight: {BODY_WEIGHT};
            text-align: center;
        }}
        QPushButton[role="watchAction"]:hover {{
            background: {colors.selected};
            border-color: {colors.focus};
        }}
        QPushButton[role="ghostAction"] {{
            background: transparent;
            border-color: transparent;
            color: {colors.text_secondary};
            text-align: center;
        }}
        QPushButton[role="ghostAction"]:hover {{
            background: {colors.surface_raised};
            color: {colors.text};
        }}
        QPushButton[role="dialogCloseAction"] {{
            background: {colors.selected};
            border-color: {colors.border};
            color: {colors.text};
            font-weight: {HEADING_WEIGHT};
            text-align: center;
        }}
        QPushButton[role="dialogCloseAction"]:hover {{
            background: {colors.card_hover};
            border-color: {colors.focus};
            color: {colors.text};
        }}
        QPushButton[role="dialogSecondaryAction"] {{
            background: {colors.surface_raised};
            border-color: {colors.border};
            color: {colors.text};
            text-align: center;
        }}
        QPushButton[role="dangerAction"] {{
            background: transparent;
            border-color: {colors.danger};
            color: {colors.danger};
            font-weight: {HEADING_WEIGHT};
            text-align: center;
        }}
        QPushButton[role="dangerAction"]:hover {{
            background: {colors.surface_raised};
            border-color: {colors.danger};
            color: {colors.text};
        }}
        QPushButton[role="organizationAction"] {{
            background: transparent;
            border-color: {colors.accent};
            color: {colors.text};
            text-align: center;
        }}
        QPushButton[role="organizationAction"]:hover {{
            background: {colors.selected};
            border-color: {colors.accent_hover};
        }}
        QPushButton[role="organizationConfirm"] {{
            background: {colors.selected};
            border-color: {colors.border};
            color: {colors.text};
            font-weight: {HEADING_WEIGHT};
            text-align: center;
        }}
        QPushButton[role="organizationConfirm"]:hover {{
            background: {colors.card_hover};
            border-color: {colors.focus};
            color: {colors.text};
        }}
        QPushButton:disabled {{
            color: {colors.text_muted};
            border-color: {colors.border};
        }}
        QPushButton[role="primaryAction"]:disabled {{
            background: {colors.surface_raised};
            color: {colors.text_muted};
            border-color: {colors.border};
        }}
        QPushButton:focus, QLineEdit:focus, QComboBox:focus, QDateEdit:focus,
        QTabBar::tab:focus {{
            border: 1px solid {colors.focus};
        }}
        QFrame[role="panel"], QFrame#personalStatePanel,
        QFrame#checkLibrarySummaryPanel, QFrame#checkLibraryIssueRow {{
            background: {colors.surface};
            border: 1px solid {colors.border};
            border-radius: {RADIUS_MEDIUM}px;
        }}
        QFrame#folderSelectionCard {{
            background: {colors.card};
            border: 1px solid {colors.border};
            border-radius: {RADIUS_MEDIUM}px;
        }}
        QFrame#movieCard {{
            background: transparent;
            border: none;
            border-radius: {RADIUS_MEDIUM}px;
        }}
        QFrame#movieCard:hover {{
            background: transparent;
            border: none;
        }}
        QFrame#libraryStateHost {{
            background: {colors.surface};
            border: 1px solid {colors.border};
            border-radius: {RADIUS_MEDIUM}px;
        }}
        QToolButton#libraryStateIcon,
        QToolButton#libraryStateIcon:disabled {{
            background: {colors.surface_raised};
            border: 1px solid {colors.border};
            border-radius: {RADIUS_MEDIUM}px;
            color: {colors.text_muted};
        }}
        QLineEdit#librarySearchInput {{
            min-height: {CONTROL_HEIGHT}px;
            max-height: {CONTROL_HEIGHT}px;
            padding: 6px 12px;
            border-radius: {RADIUS_CONTROL}px;
            background: {colors.background};
        }}
        QLineEdit#librarySearchInput:focus {{
            border: 1px solid {colors.focus};
        }}
        QFrame#tmdbSettingsCard {{
            background: {colors.surface};
            border: 1px solid {colors.border};
            border-radius: {RADIUS_MEDIUM}px;
        }}
        QFrame#appearanceSettingsCard, QFrame#historyRecoverySettingsCard {{
            background: {colors.surface};
            border: 1px solid {colors.surface_raised};
            border-radius: {RADIUS_MEDIUM}px;
        }}
        QFrame[role="settingDivider"] {{
            background: {colors.surface_raised};
            border: none;
            min-height: 1px;
            max-height: 1px;
        }}
        QFrame[role="settingSection"] {{
            background: transparent;
            border: none;
        }}
        QFrame#languageToggle {{
            background: {colors.surface};
            border: 1px solid {colors.border};
            border-radius: {RADIUS_CONTROL}px;
        }}
        QFrame#languageToggle QPushButton {{
            min-height: 30px;
            padding: 4px 10px;
            border: none;
            border-radius: {RADIUS_CONTROL}px;
        }}
        QFrame#languageToggle QPushButton:checked {{
            background: {colors.selected};
            color: {colors.text};
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#tmdbInfoBarTitle {{
            color: {colors.text};
            font-weight: {HEADING_WEIGHT};
        }}
        QFrame#personalPreferenceGroup, QFrame#personalWatchlistGroup,
        QFrame#personalWatchingGroup, QFrame#personalHistoryGroup {{
            background: transparent;
            border: none;
            border-radius: 0px;
        }}
        QFrame#historyRow, QFrame#mediaFileEntry {{
            background: transparent;
            border: none;
        }}
        QFrame[role="personalDivider"] {{
            background: {colors.border};
            border: none;
            min-height: 1px;
            max-height: 1px;
        }}
        QLabel#personalGroupHeading {{
            color: {colors.text_secondary};
            font-size: {BODY_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#checkLibrarySectionHeading {{
            color: {colors.text_secondary};
            font-size: {SMALL_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QPushButton[role="historyRemoveAction"] {{
            min-height: 28px;
            max-height: 28px;
            padding: 4px 8px;
            background: transparent;
            border: none;
            color: {colors.danger};
            font-size: {SMALL_SIZE:g}px;
        }}
        QPushButton[role="historyRemoveAction"]:hover {{
            background: {colors.surface_raised};
            color: {colors.danger};
        }}
        QFrame#mediaFileInfoSurface {{
            background: {colors.surface_raised};
            border: 1px solid {colors.border};
            border-radius: {RADIUS_CONTROL}px;
        }}
        QLabel#mediaFilenameLabel {{
            color: {colors.text};
            font-size: {BODY_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QFrame#manualSearchResultCard {{
            background: {colors.card};
            border: 1px solid {colors.border};
            border-radius: {RADIUS_MEDIUM}px;
        }}
        QFrame#manualSearchResultCard:hover {{
            background: {colors.card_hover};
            border-color: {colors.secondary};
        }}
        QFrame[role="dangerZone"] {{
            background: {colors.surface};
            border: 1px solid {colors.danger};
            border-radius: {RADIUS_MEDIUM}px;
        }}
        QLabel#dangerZoneHeading {{
            color: {colors.danger};
            font-size: {H5_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QFrame#operationHistoryContainer {{
            background: {colors.background};
            border: none;
        }}
        QFrame[role="operationRow"] {{
            background: transparent;
            border: none;
            border-bottom: 1px solid {colors.surface_raised};
            border-radius: 0px;
        }}
        QScrollArea#detailsScrollArea, QWidget#detailsContent {{
            background: {colors.background};
            border: none;
        }}
        QFrame#detailsBackBar {{
            background: {colors.surface};
            border: none;
            border-bottom: 1px solid {colors.border};
        }}
        QFrame#detailsOverviewDivider {{
            color: {colors.border};
            background: {colors.border};
            max-height: 1px;
            border: none;
        }}
        QLabel[operationState="COMMITTED"] {{ color: {colors.success}; }}
        QLabel[operationState="FAILED"], QLabel[operationState="RECOVERY_REQUIRED"] {{
            color: {colors.danger};
        }}
        QLabel[operationState="EXECUTING"], QLabel[operationState="FS_VERIFIED"] {{
            color: {colors.warning};
        }}
        QTabBar::tab {{
            color: {colors.text_muted};
            background: transparent;
            border: 1px solid transparent;
            border-radius: {RADIUS_CONTROL}px;
            padding: 6px 12px;
            min-height: {CONTROL_HEIGHT}px;
            margin-right: 4px;
        }}
        QTabBar::tab:hover {{
            color: {colors.text};
            background: {colors.card_hover};
        }}
        QTabBar::tab:selected {{
            color: {colors.text};
            background: {colors.surface_raised};
            border: 1px solid {colors.border};
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#manualSearchResultTitle {{
            color: {colors.text};
            font-size: {H5_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#manualSearchResultYear,
        QLabel#manualSearchResultId,
        QLabel#manualSearchResultRating {{
            color: {colors.text_secondary};
            font-size: {SMALL_SIZE:g}px;
        }}
        QLabel#manualSearchResultOverview {{
            color: {colors.text_muted};
            padding-top: 4px;
        }}
        QLabel#posterPlaceholder {{
            background: {colors.surface_raised};
            color: {colors.text};
            border: 1px solid {colors.border};
            border-radius: 6px;
            font-size: {H4_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#movieTitleLabel {{
            font-size: {MOVIE_TITLE_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel[role="rowTitle"] {{
            font-size: {BODY_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#detailsRatingStars, QLabel#detailsHeroRatingStar {{
            color: {colors.warning};
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#detailsRatingValue, QLabel#detailsHeroMetaLabel, QLabel#detailsHeroRatingValue {{
            color: {colors.text_muted};
            font-size: {SMALL_SIZE:g}px;
        }}
        QComboBox, QLineEdit {{
            background: {colors.surface};
            border: 1px solid {colors.border};
            border-radius: {RADIUS_SMALL}px;
            padding: 6px 8px;
            min-height: {CONTROL_HEIGHT}px;
        }}
        QComboBox QAbstractItemView {{
            background: {colors.surface_raised};
            selection-background-color: {colors.selected};
        }}
        QDateEdit {{
            background: {colors.surface};
            border: 1px solid {colors.border};
            border-radius: {RADIUS_SMALL}px;
            padding: 6px 8px;
            min-height: {CONTROL_HEIGHT}px;
        }}
        QDateEdit::drop-down {{
            width: 30px;
            border-left: 1px solid {colors.border};
        }}
        /* Keep the date popup deliberately traditional/native-like.  Theme
           only the semantic surfaces and selection; do not turn navigation
           into oversized branded controls. */
        QCalendarWidget {{
            background: {colors.surface};
            border: 1px solid {colors.border};
        }}
        QCalendarWidget QWidget#qt_calendar_navigationbar {{
            background: {colors.surface};
        }}
        QCalendarWidget QToolButton {{
            background: transparent;
            color: {colors.text};
            border: none;
            border-radius: {RADIUS_CONTROL}px;
            padding: 4px 6px;
        }}
        QCalendarWidget QToolButton:hover {{
            background: {colors.surface_raised};
        }}
        QCalendarWidget QAbstractItemView {{
            background: {colors.surface};
            color: {colors.text};
            outline: none;
            selection-background-color: {colors.selected};
            selection-color: {colors.text};
        }}
        QCheckBox {{ spacing: {SPACE_SMALL}px; }}
        QLabel#selectedFolderLabel {{
            background: {colors.background};
            border: 1px solid {colors.border};
            border-radius: {RADIUS_CONTROL}px;
            min-height: {CONTROL_HEIGHT}px;
            padding: 0 12px;
            color: {colors.text_muted};
        }}
        QLabel#importResultsCount {{
            color: {colors.text_muted};
            font-size: {SMALL_SIZE:g}px;
        }}
        QFrame#importReviewHeader {{
            background: {colors.surface_raised};
            border: 1px solid {colors.border};
            border-bottom: none;
            border-top-left-radius: {RADIUS_MEDIUM}px;
            border-top-right-radius: {RADIUS_MEDIUM}px;
        }}
        QFrame#importReviewRow {{
            background: {colors.card};
            border-left: 1px solid {colors.border};
            border-right: 1px solid {colors.border};
            border-bottom: 1px solid {colors.border};
            border-radius: 0px;
        }}
        QFrame#importReviewRow:hover {{
            background: {colors.card_hover};
        }}
        QLabel#importPathLabel {{
            color: {colors.text_muted};
            font-size: {SMALL_SIZE:g}px;
        }}
        QLabel#movieAvailabilityLabel[availability="MISSING"] {{ color: {colors.danger}; }}
        QLabel#movieAvailabilityLabel[availability="PARTIAL"] {{ color: {colors.warning}; }}
        QLabel#movieAvailabilityLabel[availability="PERSONAL"] {{ color: {colors.text_muted}; }}
        QLabel#importStatusLabel {{
            color: {colors.text};
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#importStatusLabel[proposalStatus="METADATA_UNAVAILABLE"],
        QLabel#importStatusLabel[proposalStatus="NO_MATCH"] {{
            color: {colors.danger};
        }}
        QLabel#libraryStateLabel, QLabel#detailsStateLabel {{
            color: {colors.text_muted};
            font-size: {BODY_SIZE:g}px;
            padding: {SPACE_MEDIUM}px;
        }}
        QScrollArea, QScrollArea#settingsScrollArea,
        QScrollArea#settingsScrollArea > QWidget,
        QWidget#settingsScrollViewport, QWidget#settingsContent,
        QWidget#settingsCardsHost, QWidget#movieGridContainer,
        QWidget#movieGridViewport, QWidget#importReviewContainer,
        QWidget#manualSearchResultsHost {{
            border: none;
            background: {colors.background};
        }}
        QScrollBar:vertical {{
            background: {colors.background};
            width: 8px;
        }}
        QScrollBar::handle:vertical {{
            background: {colors.border};
            border-radius: 4px;
            min-height: 24px;
        }}
        QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical {{
            height: 0px;
            background: transparent;
        }}
        QProgressBar {{
            background: {colors.surface};
            border: 1px solid {colors.border};
            border-radius: {RADIUS_SMALL}px;
            text-align: center;
            color: {colors.text};
        }}
        QProgressBar::chunk {{
            background: {colors.primary};
            border-radius: {RADIUS_SMALL}px;
        }}
        QLabel#libraryCheckPercentageLabel {{
            color: {colors.text_secondary};
            font-size: {SMALL_SIZE:g}px;
        }}
        QLabel#libraryCheckIdleDescription, QLabel#libraryCheckFailureLabel {{
            color: {colors.text_secondary};
            line-height: 1.3em;
        }}
        QLabel#libraryCheckFailureLabel {{
            color: {colors.danger};
        }}
        QLabel#libraryCheckSummaryLabel {{
            color: {colors.text_secondary};
            line-height: 1.3em;
        }}
        QLabel#checkLibraryIssueTitle {{
            color: {colors.text};
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#checkLibraryIssueDetail, QLabel#checkLibraryIssueOutcome {{
            color: {colors.text_secondary};
        }}
        QScrollArea#checkLibraryIssuesScroll, QScrollArea#libraryCheckPageIssuesScroll {{
            background: {colors.background};
            border: none;
            max-height: 220px;
        }}
        QLabel#libraryCheckPageDescription, QLabel#libraryCheckPageStatusLabel,
        QLabel#libraryCheckPageFailureLabel {{
            color: {colors.text_secondary};
        }}
        QLabel#libraryCheckPageFailureLabel {{ color: {colors.danger}; }}
        QLabel#libraryCheckPagePercentageLabel {{
            color: {colors.text_secondary};
            font-size: {SMALL_SIZE:g}px;
        }}
        QLabel#libraryCheckPassedLabel, QLabel#libraryCheckNeedsAttentionLabel {{
            font-size: {H5_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#personalEmptyStateIcon {{
            color: {colors.accent};
            font-size: {H2_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#personalEmptyStateTitle {{
            color: {colors.text};
            font-size: {H4_SIZE:g}px;
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#personalEmptyStateDescription {{
            color: {colors.text_secondary};
        }}
        QFrame#personalEmptyState {{
            background: transparent;
            border: none;
        }}
        QLabel#mediaStatusLabel {{
            padding: 3px 8px;
            border-radius: {RADIUS_CONTROL}px;
            font-size: {SMALL_SIZE:g}px;
        }}
        QLabel#mediaStatusLabel[availability="MISSING"] {{
            color: {colors.danger};
            background: {colors.surface_raised};
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#movieAvailabilityLabel[availability="MISSING"],
        QLabel#movieAvailabilityLabel[availability="PARTIAL"] {{
            color: {colors.danger};
            font-weight: {HEADING_WEIGHT};
        }}
        QLabel#mediaStatusLabel[availability="PRESENT"] {{
            color: {colors.success};
            background: {colors.surface_raised};
            font-weight: {HEADING_WEIGHT};
        }}
    """


def apply_theme(application: QApplication, theme_id: UiTheme | str = UiTheme.MAIN) -> None:
    try:
        selected = UiTheme(theme_id)
    except (TypeError, ValueError):
        selected = UiTheme.MAIN
    application.setStyle("Fusion")
    application.setProperty("dropsortBaseStyle", "Fusion")
    application.setProperty("dropsortTheme", selected.value)
    registered = register_application_fonts()
    application.setProperty(
        "dropsortFontFamily",
        FONT_FAMILY if registered else FALLBACK_FONT_FAMILY,
    )
    application.setProperty(
        "dropsortArabicFontFamily",
        ARABIC_FONT_FAMILY if registered else FALLBACK_FONT_FAMILY,
    )
    application.setStyleSheet(application_stylesheet(selected))
