from __future__ import annotations

import re

from PySide6.QtCore import Qt
from PySide6.QtWidgets import QWidget

from dropsort.application.configuration.localization import UiLanguage
from dropsort.ui.common.formatting import format_rating, format_year, to_western_numerals
from dropsort.ui.localization import TextId, UiLocalizer, _ARABIC, _ENGLISH


_PLACEHOLDER_RE = re.compile(r"\{([A-Za-z_][A-Za-z0-9_]*)\}")
_EASTERN_DIGITS = "٠١٢٣٤٥٦٧٨٩"


APPROVED_ARABIC = {
    TextId.WINDOW_TITLE: "مكتبة DropSort للأفلام",
    TextId.BRAND_SUBTITLE: "مكتبة أفلام محلية",
    TextId.NAV_LIBRARY: "المكتبة",
    TextId.NAV_ADD_MOVIES: "إضافة أفلام",
    TextId.NAV_HISTORY: "سجل العمليات",
    TextId.NAV_SETTINGS: "الإعدادات",
    TextId.NAV_PERSONAL_LIBRARY: "مكتبتي",
    TextId.LIBRARY_HEADING: "مكتبتك",
    TextId.PERSONAL_LIBRARY_HEADING: "مكتبتي",
    TextId.SETTINGS_TITLE: "الإعدادات",
    TextId.APPEARANCE: "المظهر",
    TextId.THEME: "النمط",
    TextId.THEME_MAIN: "الأساسي",
    TextId.THEME_DARK: "الداكن",
    TextId.THEME_SLATE: "سليت",
    TextId.THEME_LIGHT: "الفاتح",
    TextId.LANGUAGE_TITLE: "اللغة",
    TextId.LANGUAGE_DESCRIPTION: "اختر لغة واجهة DropSort.",
    TextId.LANGUAGE_ENGLISH: "الإنجليزية",
    TextId.LANGUAGE_ARABIC: "العربية",
    TextId.LANGUAGE_ACCESSIBLE: "اللغة: الإنجليزية أو العربية",
    TextId.DANGER_ZONE: "إجراءات حساسة",
    TextId.LIBRARY_DATA: "بيانات المكتبة",
    TextId.CLEAR_LIBRARY_DESCRIPTION: "امسح بيانات الأفلام والملفات المفهرسة من DropSort. لن يتم حذف ملفات أفلامك الفعلية.",
    TextId.CLEAR_LIBRARY: "مسح بيانات المكتبة",
    TextId.CLEAR_LIBRARY_TITLE: "مسح بيانات المكتبة؟",
    TextId.CLEAR_LIBRARY_CONFIRM: "سيتم مسح روابط ملفات الوسائط المحلية وذاكرة البيانات الوصفية وذاكرة الملصقات. ستظل الأفلام المرتبطة ببياناتك الشخصية محفوظة، بينما ستُزال من المكتبة الأفلام التي لا ترتبط ببيانات شخصية محفوظة. لن يحذف DropSort ملفات أفلامك الفعلية أو ينقلها أو يعيد تسميتها أو ينسخها أو يعدّلها. سيظل سجل العمليات وبيانات الاسترداد محفوظين. هل تريد المتابعة؟",
    TextId.CLEAR_LIBRARY_RUNNING: "جارٍ مسح بيانات المكتبة المحلية...",
    TextId.CLEAR_LIBRARY_RESULT: "تم مسح المكتبة: أُزيل {movies} فيلمًا و{files} رابطًا لملفات الوسائط.",
    TextId.CLEAR_LIBRARY_CACHE_WARNING: "تعذر إكمال تنظيف ذاكرة الملصقات؛ لم تتأثر ملفات الوسائط.",
    TextId.TMDB_METADATA: "بيانات TMDB",
    TextId.TMDB_NOT_CONFIGURED: "غير مُعد",
    TextId.TMDB_ENVIRONMENT: "تم الإعداد من متغيرات النظام",
    TextId.TMDB_SESSION: "مُعد لهذه الجلسة",
    TextId.TMDB_SESSION_NOTICE: "الرمز الذي تدخله هنا يُستخدم خلال جلسة التطبيق الحالية فقط ولا يتم حفظه بشكل دائم.",
    TextId.TMDB_TOKEN_PLACEHOLDER: "رمز وصول القراءة من TMDB",
    TextId.TMDB_SAVE_SESSION: "استخدام في هذه الجلسة",
    TextId.TMDB_CLEAR_SESSION: "مسح رمز الجلسة",
    TextId.TMDB_INFOBAR_TITLE: "إعداد TMDB",
    TextId.TMDB_INFOBAR_DESCRIPTION: "يستخدم DropSort خدمة TMDB لجلب معلومات الأفلام والملصقات. تعليمات الإعداد الأساسية متاحة داخل التطبيق دون اتصال.",
    TextId.TMDB_SETUP_GUIDE: "دليل الإعداد",
    TextId.TMDB_OPEN_OFFICIAL: "فتح TMDB",
    TextId.TMDB_RATING_LABEL: "TMDB",
    TextId.TMDB_RATING_UNAVAILABLE: "تقييم TMDB غير متاح",
    TextId.LIBRARY_SEARCH_PLACEHOLDER: "ابحث في مكتبتك...",
    TextId.LIBRARY_SEARCH_CLEAR: "مسح بحث المكتبة",
    TextId.LIBRARY_SEARCH_NO_RESULTS: "لم يتم العثور على أفلام",
    TextId.LIBRARY_SEARCH_SUGGESTION: "اقتراح من أفلام مكتبتك",
    TextId.LIBRARY_EMPTY: "مكتبتك فارغة حاليًا.",
    TextId.LIBRARY_LOAD_ERROR: "تعذر على DropSort تحميل المكتبة المحلية. حاول مرة أخرى.",
    TextId.CHECK_LIBRARY_FILES: "فحص المكتبة",
    TextId.MISSING_FILE: "الملف غير موجود",
    TextId.MISSING_FILES: "{count} ملفات غير موجودة",
    TextId.DETAILS_BACK: "رجوع",
    TextId.DETAILS_OVERVIEW: "نبذة",
    TextId.DETAILS_MEDIA_FILES: "ملفات الوسائط",
    TextId.DETAILS_ORIGINAL_TITLE: "العنوان الأصلي: {title}",
    TextId.DETAILS_YOUR_LIBRARY: "مكتبتك",
    TextId.DETAILS_PREFERENCE_GROUP: "رأيك",
    TextId.DETAILS_WATCHLIST_GROUP: "قائمة المشاهدة",
    TextId.DETAILS_WATCHING_GROUP: "المشاهدة",
    TextId.DETAILS_LIKE: "أعجبني",
    TextId.DETAILS_BLACKLIST: "استبعاد",
    TextId.DETAILS_CLEAR_PREFERENCE: "إلغاء الاختيار",
    TextId.DETAILS_ADD_WATCHLIST: "إضافة إلى قائمة المشاهدة",
    TextId.DETAILS_IN_WATCHLIST: "في قائمة المشاهدة",
    TextId.DETAILS_MARK_WATCHED: "تسجيل المشاهدة",
    TextId.DETAILS_MARK_WATCHED_DATE: "تسجيل مشاهدة بتاريخ محدد",
    TextId.DETAILS_WATCH_DATE: "تاريخ المشاهدة",
    TextId.DETAILS_WATCH_DATE_FUTURE: "اختر تاريخ اليوم أو تاريخًا سابقًا.",
    TextId.DETAILS_WATCHED_COUNT: "عدد مرات المشاهدة: {count}",
    TextId.DETAILS_LAST_WATCHED: "آخر مشاهدة: {date}",
    TextId.DETAILS_NOT_WATCHED: "لم تتم مشاهدته بعد",
    TextId.DETAILS_WATCH_HISTORY: "سجل المشاهدة",
    TextId.DETAILS_FIRST_WATCH: "أول مشاهدة",
    TextId.DETAILS_REWATCH: "إعادة مشاهدة",
    TextId.DETAILS_REMOVE_WATCH_EVENT: "إزالة",
    TextId.PERSONAL_TAB_WATCHLIST: "قائمة المشاهدة",
    TextId.PERSONAL_TAB_READY: "جاهزة للمشاهدة",
    TextId.PERSONAL_TAB_LIKED: "أعجبتني",
    TextId.PERSONAL_TAB_BLACKLISTED: "المستبعدة",
    TextId.PERSONAL_EMPTY_WATCHLIST: "قائمة المشاهدة فارغة.",
    TextId.PERSONAL_EMPTY_WATCHLIST_DESCRIPTION: "الأفلام التي تحفظها لوقت لاحق ستظهر هنا.",
    TextId.PERSONAL_EMPTY_READY: "لا توجد أفلام جاهزة للمشاهدة حاليًا.",
    TextId.PERSONAL_EMPTY_READY_DESCRIPTION: "الأفلام الموجودة في قائمة المشاهدة والمتوفرة محليًا ستظهر هنا.",
    TextId.ADD_MOVIES_TITLE: "إضافة أفلام",
    TextId.ADD_MOVIES_GUIDANCE: "اختر مجلدًا. سيفحصه DropSort للقراءة فقط، ثم يجهز اقتراحات بيانات الأفلام لمراجعتها.",
    TextId.CHOOSE_MOVIE_FOLDER: "اختر مجلد الأفلام",
    TextId.CHOOSE_FOLDER_SCAN: "اختيار مجلد وبدء الفحص",
    TextId.NO_FOLDER: "لم يتم اختيار مجلد",
    TextId.SCAN_READY: "اختر مجلدًا لبدء فحص للقراءة فقط.",
    TextId.CANCEL_SCAN: "إلغاء الفحص",
    TextId.INCLUDE_SUBFOLDERS: "تضمين المجلدات الفرعية",
    TextId.SCAN_PROGRESS_DISCOVERY: "المجلدات: {folders} · الملفات التي تم فحصها: {files} · ملفات الأفلام المحتملة: {movies}",
    TextId.SCAN_COMPLETE: "اكتمل الفحص. الملفات التي تم فحصها: {files}. جاهز للإضافة أو المراجعة: {ready}. موجود بالفعل في المكتبة: {existing}. الأخطاء: {errors}.",
    TextId.SCAN_CANCELLED: "تم إلغاء الفحص. الملفات التي تم فحصها قبل الإلغاء: {files}. تم تجاهل النتائج الجزئية. لم يتم تغيير أي ملفات.",
    TextId.SEARCH_MANUALLY: "بحث يدوي",
    TextId.SEARCH_TMDB: "البحث في TMDB",
    TextId.SELECT_THIS_MOVIE: "اختيار هذا الفيلم",
    TextId.YEAR: "السنة (اختياري)",
    TextId.INVALID_YEAR: "أدخل سنة صحيحة من 4 أرقام أو اترك الحقل فارغًا.",
    TextId.HISTORY_RECOVERY: "السجل والاسترداد",
    TextId.HISTORY_TITLE: "سجل العمليات",
    TextId.HISTORY_GUIDANCE: "أحدث عمليات الملفات التي نفذها DropSort.",
    TextId.REFRESH: "تحديث",
    TextId.HISTORY_COPY: "نسخ",
    TextId.HISTORY_SAVE: "حفظ",
    TextId.HISTORY_SELECT: "تحديد العملية",
    TextId.HISTORY_EMPTY: "لا توجد عمليات ملفات حتى الآن.\n\nستظهر هنا عمليات نقل الملفات وغيرها من العمليات التي يديرها DropSort.",
    TextId.HISTORY_COPY_EMPTY: "حدد عملية لنسخها.",
    TextId.HISTORY_SAVE_SUCCESS: "تم حفظ سجل العمليات.",
    TextId.HISTORY_SAVE_ERROR: "تعذر حفظ سجل العمليات.",
    TextId.HISTORY_FROM: "من",
    TextId.HISTORY_TO: "إلى",
    TextId.HISTORY_OPERATION_ID: "معرّف العملية",
    TextId.HISTORY_OPERATION_MOVE: "نقل",
    TextId.HISTORY_OPERATION_RENAME: "إعادة تسمية",
    TextId.HISTORY_STATUS_PLANNED: "مخطط لها",
    TextId.HISTORY_STATUS_VALIDATED: "تم التحقق",
    TextId.HISTORY_STATUS_IN_PROGRESS: "قيد التنفيذ",
    TextId.HISTORY_STATUS_VERIFIED: "تم التأكد",
    TextId.HISTORY_STATUS_COMPLETED: "مكتملة",
    TextId.HISTORY_STATUS_FAILED: "فشلت",
    TextId.HISTORY_STATUS_RECOVERY_REQUIRED: "يلزم الاسترداد",
    TextId.OPERATION_DETAILS: "تفاصيل العملية",
    TextId.DETAILS: "التفاصيل",
    TextId.CLOSE: "إغلاق",
    TextId.HISTORY_READ_ONLY: "تفاصيل السجل للعرض فقط.",
    TextId.HISTORY_FIELD_STATE: "الحالة",
    TextId.HISTORY_FIELD_OPERATION: "العملية",
    TextId.HISTORY_FIELD_SOURCE: "المصدر",
    TextId.HISTORY_FIELD_DESTINATION: "الوجهة",
    TextId.HISTORY_FIELD_STRATEGY: "طريقة التنفيذ",
    TextId.HISTORY_FIELD_CURRENT_PATH: "المسار الحالي في المكتبة",
    TextId.HISTORY_FIELD_CREATED: "تاريخ الإنشاء",
    TextId.HISTORY_NOT_RECORDED: "غير مسجل",
    TextId.HISTORY_NOT_LINKED: "غير مرتبط",
    TextId.PREVIEW_UNDO: "معاينة التراجع",
    TextId.INSPECT_RECOVERY: "فحص الاسترداد",
    TextId.ATTEMPT_RECOVERY: "محاولة استرداد آمنة",
    TextId.CHECK_FILES_TITLE: "فحص المكتبة",
    TextId.CHECK_FILES_READY: "جاهز لفحص ملفات المكتبة.",
    TextId.CHECK_FILES_CANCEL: "إلغاء الفحص",
    TextId.CHECK_FILES_RUNNING: "جارٍ فحص ملفات المكتبة وبيانات الأفلام...",
    TextId.CHECK_FILES_CANCELLED: "تم إلغاء الفحص",
    TextId.CHECK_LIBRARY_ISSUES_SECTION: "مشكلات تحتاج مراجعة",
    TextId.CHECK_LIBRARY_RESULTS: "النتائج",
    TextId.CHECK_LIBRARY_ISSUE: "المشكلة",
    TextId.CHECK_LIBRARY_OUTCOME: "النتيجة",
    TextId.CHECK_LIBRARY_AGAIN: "إعادة الفحص",
    TextId.ACCESSIBILITY_TMDB_RATING_VISUAL: "عرض تقييم TMDB",
    TextId.ACCESSIBILITY_OPERATION_PATHS: "مسارات المصدر والوجهة للعملية",
    TextId.ACCESSIBILITY_WATCHED_DATE_CALENDAR: "تقويم تاريخ المشاهدة",
}


def test_approved_arabic_copy_matches_the_source_of_truth() -> None:
    assert set(APPROVED_ARABIC).issubset(set(TextId))
    assert {key: _ARABIC[key] for key in APPROVED_ARABIC} == APPROVED_ARABIC


def test_catalogs_have_parity_and_preserve_placeholders() -> None:
    assert UiLocalizer().missing_translations() == ()
    assert set(_ENGLISH) == set(_ARABIC) == set(TextId)
    for key in TextId:
        assert set(_PLACEHOLDER_RE.findall(_ENGLISH[key])) == set(
            _PLACEHOLDER_RE.findall(_ARABIC[key])
        ), key


def test_arabic_localizer_is_rtl_and_technical_values_are_ltr_safe(qapp) -> None:
    localizer = UiLocalizer(UiLanguage.ARABIC)
    assert localizer.text(TextId.DETAILS_ORIGINAL_TITLE, title="Blade Runner") == (
        "العنوان الأصلي: Blade Runner"
    )
    assert localizer.text(TextId.CLEAR_LIBRARY_RESULT, movies=12, files=34) == (
        "تم مسح المكتبة: أُزيل 12 فيلمًا و34 رابطًا لملفات الوسائط."
    )
    assert qapp.layoutDirection() == Qt.LayoutDirection.RightToLeft
    technical = QWidget()
    localizer.mark_ltr(technical)
    assert technical.layoutDirection() == Qt.LayoutDirection.LeftToRight
    assert technical.property("dropsortTechnicalLtr") is True


def test_numeric_ui_output_uses_western_digits_only() -> None:
    output = " ".join(
        (
            to_western_numerals("١٢٣"),
            format_year(2024),
            format_rating(8.5),
        )
    )
    assert output == "123 2024 8.5 / 10"
    assert not any(digit in output for digit in _EASTERN_DIGITS)
