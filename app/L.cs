using System.Globalization;
using SaberBridge.Core;

namespace SaberBridge;

/// <summary>UI strings: Russian when the system language is Russian, English otherwise.</summary>
public static class L
{
    public static bool Ru { get; private set; } = (Core.Settings.Load().Language ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) == "ru";

    /// <summary>Switches the language; the windows are rebuilt afterwards so their fixed texts follow.</summary>
    public static void Use(string code) => Ru = code == "ru";

    private static string T(string ru, string en) => Ru ? ru : en;

    public static string Subtitle => T("Конвертер для Beat Saber: аватары для ПК, сейберы и ноты для Quest", "Beat Saber converter: PC avatars, Quest sabers and notes");
    public static string DropTitle => T("Перетащи модели сюда", "Drop your models here");
    public static string DropHint => T(".vrm  ·  .unitypackage  ·  .pmx  ·  .zip  ·  папки", ".vrm  ·  .unitypackage  ·  .pmx  ·  .zip  ·  folders");
    public static string Browse => T("Выбрать файлы", "Choose files");
    public static string Queue => T("Очередь", "Queue");
    public static string Clear => T("Очистить", "Clear");
    public static string InstallAll => T("Установить все", "Install all");
    public static string Install => T("Установить", "Install");
    public static string Installed => T("Установлен", "Installed");
    public static string OpenFolder => T("Открыть папку", "Open folder");
    public static string Convert => T("Конвертировать", "Convert");
    public static string Cancel => T("Отмена", "Cancel");
    public static string Log => T("Журнал", "Log");
    public static string Settings => T("Настройки", "Settings");
    public static string Game => T("Beat Saber", "Beat Saber");
    public static string GameHint => T("Папка игры с модом Custom Avatars. Отсюда берутся FinalIK и DynamicBone, сюда ставятся аватары.", "Game folder with the Custom Avatars mod. FinalIK and DynamicBone are taken from it, avatars are installed into it.");
    public static string UnityHint => T("Unity 2021.3.16f1 через Unity Hub (бесплатная лицензия Personal). Окно Unity открывать не нужно, приложение запускает его в фоне.", "Unity 2021.3.16f1 via Unity Hub (free Personal license). You never need to open it; the app runs it in the background.");
    public static string GetUnity => T("Скачать Unity Hub", "Get Unity Hub");
    public static string Change => T("Изменить…", "Change…");
    public static string AutoInstall => T("Сразу ставить готовые аватары в игру", "Install finished avatars into the game right away");
    public static string Language => T("Язык", "Language");
    public static string DataFolder => T("Папка данных приложения", "App data folder");
    public static string Close => T("Готово", "Done");
    public static string UnityMissing => T("Unity 2021.3 не найден", "Unity 2021.3 not found");
    public static string GameMissing => T("Beat Saber не найден", "Beat Saber not found");
    public static string NoCustomAvatars => T("нет мода Custom Avatars", "Custom Avatars mod missing");

    public static string Waiting => T("ждёт", "waiting");
    public static string Working => T("конвертируется…", "converting…");
    public static string Done => T("готово", "done");
    public static string Failed => T("ошибка", "failed");
    public static string Skipped => T("пропущен", "skipped");
    public static string Unsupported => T("не поддерживается", "not supported");

    // the same line on both tabs: Unity is ready and nothing waits
    public static string Ready => T("Готово к конвертации. Оригиналы не изменяются и не перемещаются.", "Ready to convert. Originals are never changed or moved.");
    public static string Scanning => T("Разбираю файлы…", "Reading files…");
    public static string Finished(int ok, int failed) => T($"Готово! Аватаров: {ok}" + (failed > 0 ? $", ошибок: {failed}" : ""), $"Finished: {ok} avatars" + (failed > 0 ? $", {failed} failed" : ""));
    public static string InstalledTo(int n, string dir) => T($"Установлено в игру: {n}  →  {dir}", $"Installed {n} into {dir}");
    public static string FirstRunHint => T("Unity один раз готовит проект: 5–10 минут. Следующие запуски — около 30 секунд.", "Unity prepares its project once: 5–10 minutes. Later runs start in about 30 seconds.");
    public static string ModelOf(string model, int i, int n) => T($"{model}  ·  модель {i} из {n}", $"{model}  ·  model {i} of {n}");

    public static string Phase(string phase) => phase switch
    {
        "prepare" => T("Готовлю проект конвертера", "Preparing the converter project"),
        "unpack" => T("Распаковываю пакет", "Unpacking the package"),
        "first-import" => T("Первый запуск: Unity импортирует проект", "First run: Unity is importing the project"),
        "unity-start" => T("Запускаю Unity", "Starting Unity"),
        "model" => T("Начинаю", "Starting"),
        "stage:import" => T("Читаю модель", "Reading the model"),
        "stage:skeleton" => T("Скелет и пропорции", "Skeleton and proportions"),
        "stage:physics" => T("Физика волос и хвоста", "Hair and tail physics"),
        "stage:materials" => T("Материалы и текстуры", "Materials and textures"),
        "stage:ik" => T("Трекеры и IK тела", "Trackers and body IK"),
        "stage:cover" => T("Обложка", "Cover"),
        "stage:bundle" => T("Упаковываю .avatar", "Packing the .avatar"),
        "model-done" => T("Модель готова", "Model finished"),
        "preview" => T("Рендерю превью и проверяю .avatar", "Rendering the preview and checking the .avatar"),
        _ => "",
    };

    public static string Cancelled => T("Отменено", "Cancelled");
    public static string NothingToDo => T("Добавь модели в очередь", "Add some models first");

    // ---- tabs and Quest sabers
    public static string TabPc => T("ПК", "PC");
    public static string TabQuest => T("Quest", "Quest");
    public static string Sabers => T("Сейберы", "Sabers");
    public static string Notes => T("Ноты", "Notes");
    public static string KindSabers => T("Сейберы  ·  .saber → .whacker", "Sabers  ·  .saber → .whacker");
    public static string KindNotes => T("Ноты  ·  .bloq → .cyoob", "Notes  ·  .bloq → .cyoob");
    public static string NoteDropTitle => T("Перетащи ноты .bloq сюда", "Drop .bloq files here");
    public static string NoteDropHint => T(".bloq (ПК, Custom Notes)  →  .cyoob (Quest, Qosmetics)", ".bloq (PC, Custom Notes)  →  .cyoob (Quest, Qosmetics)");
    public static string NotesEmpty => T("Перетащи сюда ноты с ПК (.bloq) — они появятся в этом списке.", "Drop PC notes (.bloq) here; they will show up in this list.");
    public static string QuestQueue => T("Quest", "Quest");
    public static string QuestDropTitle => T("Перетащи сюда сейберы, ноты и аватары", "Drop sabers, notes and avatars here");
    public static string QuestDropHint => T(".saber → .whacker  ·  .bloq → .cyoob  ·  .avatar → .vrm", ".saber → .whacker  ·  .bloq → .cyoob  ·  .avatar → .vrm");
    public static string QuestEmpty => T("Перетащи сюда сейберы (.saber), ноты (.bloq) и аватары (.avatar) с ПК — сразу пачкой, вперемешку. Одна кнопка «Конвертировать» сделает всё. Аватары становятся .vrm для мода VRM Qavatars.", "Drop PC sabers (.saber), notes (.bloq) and avatars (.avatar) here, as many and as mixed as you like. One Convert press makes them all. Avatars become .vrm for the VRM Qavatars mod.");
    public static string AvatarNeedsGame => T("Чтобы прочитать .avatar, нужен Beat Saber на этом ПК с модом Custom Avatars (из него берутся FinalIK и DynamicBone). Укажи папку игры вверху.", "Reading .avatar files needs Beat Saber on this PC with the Custom Avatars mod (FinalIK and DynamicBone come from it). Choose the game folder with the Beat Saber button at the top.");
    public static string AddedToOtherQueue(int n, string tab) => T($"{n} файл(ов) добавлено в очередь «{tab}» — они ждут там.", $"{n} file(s) added to the {tab} queue; they wait there.");
    public static string ChkAndroid => T("Модуль Android для Unity", "Unity Android module");
    public static string ChkAndroidOk => T("Установлен: Unity может собирать файлы для Quest.", "Installed: Unity can build files for the Quest.");
    public static string ChkAndroidMissing => T("Нет модуля «Android Build Support»: без него Unity не соберёт файлы для Quest.", "\"Android Build Support\" is missing; Unity needs it to build files for the Quest.");
    public static string ActInstallAndroid => T("Скачать и установить модуль (≈ 600 МБ)", "Download and install the module (about 600 MB)");
    public static string SaberDropTitle => T("Перетащи сейберы .saber сюда", "Drop .saber files here");
    public static string SaberDropHint => T(".saber (ПК)  →  .whacker (Quest, Qosmetics)", ".saber (PC)  →  .whacker (Quest, Qosmetics)");
    public static string SabersEmpty => T("Перетащи сюда сейберы с ПК (.saber) — они появятся в этом списке.", "Drop PC sabers (.saber) here; they will show up in this list.");
    public static string QuestFolderHint => T("Готовые сейберы кладутся в подпапку Sabers, ноты — в Notes, аватары — в Avatars. Отсюда их ставишь на Quest через Mods Before Friday.", "Finished sabers go to the Sabers subfolder, notes to Notes, avatars to Avatars. Install them on the Quest with Mods Before Friday.");
    public static string QuestFolder => T("Папка для Quest", "Quest folder");
    public static string SaberConverted => T("сконвертирован", "converted");
    public static string SaberNotConverted => T("не сконвертирован", "not converted");
    public static string SaberOutputMissing => T("нет сконвертированного файла", "converted file missing");
    public static string SaberSourceMissing(string path) => T($"Оригинал пропал: {path}", $"Original is gone: {path}");
    public static string SaberSummary(int all, int done, int missing) => T($"Всего {all} · готово {done}" + (missing > 0 ? $" · пропало {missing}" : ""), $"{all} total · {done} done" + (missing > 0 ? $" · {missing} missing" : ""));
    public static string SaberEngineNote => T("Оригиналы не изменяются и не перемещаются.", "Originals are never changed or moved.");
    public static string SaberNeedsUnity => T("Для конвертации нужен модуль Android Build Support для Unity 2021.3.16f1 (Unity Hub → Installs → ⚙ → Add modules). Оригиналы .saber не изменяются.", "Converting needs the Android Build Support module for Unity 2021.3.16f1 (Unity Hub → Installs → ⚙ → Add modules). Original .saber files are never changed.");
    public static string SaberReady => Ready;
    public static string PickGame => T("Указать папку игры…", "Choose the game folder…");
    public static string SaberFirstRun => T("Первый запуск: Unity готовит проект для Quest, это несколько минут.", "First run: Unity is preparing the Quest project, this takes a few minutes.");
    public static string SaberConverting(string what) => T($"Конвертирую {what}", $"Converting {what}");
    public static string SaberFinished(int ok, int failed) => T($"Готово: {ok}" + (failed > 0 ? $", ошибок: {failed}" : "") + ". Файлы лежат в папке для Quest.", $"Done: {ok}" + (failed > 0 ? $", {failed} failed" : "") + ". The files are in the Quest folder.");
    public static string ShowOriginal => T("Где оригинал", "Show original");
    public static string Forget => T("Убрать из списка", "Remove from list");
    public static string Refresh => T("Обновить", "Refresh");

    // ---- library
    public static string Library => T("Аватары", "Avatars");
    public static string LibraryEmpty => T("Здесь будут аватары из игры и только что сконвертированные.\nПеретащи модели слева и нажми «Конвертировать».", "Avatars from the game and freshly converted ones show up here.\nDrop models on the left and press Convert.");
    public static string Search => T("Поиск", "Search");
    public static string New => T("НОВЫЙ", "NEW");
    public static string ReplaceInGame => T("Заменить в игре", "Replace in game");
    public static string OldInGame => T("в игре старая версия", "older version in game");
    public static string NotInstalled => T("не установлен", "not installed");
    public static string Preview => T("Превью", "Preview");
    public static string Delete => T("Удалить", "Delete");
    public static string PreviewAll(int n) => T($"Сделать превью ({n})", $"Make previews ({n})");
    public static string OpenGameFolder => T("Папка игры", "Game folder");
    public static string MakingPreviews(int i, int n) => T($"Готовлю превью {i} из {n}", $"Preparing preview {i} of {n}");
    public static string PreviewsDone => T("Превью готовы", "Previews ready");
    public static string DeleteTitle => T("Удалить аватар?", "Delete this avatar?");
    public static string DeleteFromGame(string name) => T($"«{name}» будет убран из игры и перемещён в корзину. Его можно будет восстановить оттуда.", $"\"{name}\" will be removed from the game and moved to the recycle bin, where you can restore it.");
    public static string DeleteConverted(string name) => T($"Файл «{name}» будет перемещён в корзину.", $"\"{name}\" will be moved to the recycle bin.");
    public static string Deleted(string name) => T($"«{name}» перемещён в корзину", $"\"{name}\" moved to the recycle bin");

    // ---- preview window
    public static string AnimTurn => T("Поворот", "Turntable");
    public static string AnimIdle => T("Стойка", "Idle");
    public static string AnimWave => T("Привет", "Wave");
    public static string AnimWalk => T("Шаг", "Walk");
    public static string Skeleton => T("Скелет", "Skeleton");
    public static string SkeletonOnly => T("Только скелет", "Skeleton only");
    public static string Play => T("Пауза", "Pause");
    public static string LegendBones => T("кости тела", "body bones");
    public static string LegendPhysics => T("физика (волосы, хвост, уши)", "physics (hair, tail, ears)");
    public static string Checks => T("Проверка .avatar", "Avatar check");
    public static string PreviewMissing => T("Превью ещё нет. Unity загрузит .avatar так же, как это делает мод, проверит его и нарисует кадры (около минуты).", "No preview yet. Unity will load the .avatar the same way the mod does, check it and render frames (about a minute).");
    public static string MakePreview => T("Сделать превью", "Make preview");
    public static string DragToRotate => T("Тяни мышью, чтобы повернуть", "Drag to rotate");
    public static string By(string author) => T($"автор: {author}", $"by {author}");
    public static string PhysicsNote => T("Физика в превью не качается — оранжевые кости показывают, что будет двигаться в игре.", "Physics doesn't sway in the preview; orange bones show what will move in game.");

    public static string CheckText(string code, string value)
    {
        string[] v = value.Split('|');
        return code switch
        {
            "bundle" => T("Файл загружается как AssetBundle", "Loads as an asset bundle"),
            "prefab" => T("Внутри нет объекта _CustomAvatar", "No _CustomAvatar object inside"),
            "descriptor" => T($"Имя: {value}", $"Name: {value}"),
            "cover" => T("Обложка для меню", "Menu cover"),
            "trackers" => value == "6/6" ? T("Трекеры всего тела (6/6)", "Full-body trackers (6/6)") : T($"Трекеры: {value} — только верх тела", $"Trackers: {value}, upper body only"),
            "ik" => T("IK тела (VRIK) настроен", "Body IK (VRIK) set up"),
            "fingers" => T("Позы пальцев (кулак / открытая ладонь)", "Finger poses (fist / open hand)"),
            "physics" => T($"Физика: {v[0]} цепочек, {v.ElementAtOrDefault(1)} костей", $"Physics: {v[0]} chains, {v.ElementAtOrDefault(1)} bones"),
            "firstperson" => T($"Скрыто от первого лица: {value} объектов", $"Hidden in first person: {value} objects"),
            "scripts" => T($"Потерянные скрипты: {value}", $"Missing scripts: {value}"),
            "materials" => v.ElementAtOrDefault(1) is "0" or null ? T($"Материалы: {v[0]}, все шейдеры работают", $"Materials: {v[0]}, all shaders work") : T($"Материалы: {v[0]}, сломанных шейдеров: {v[1]}", $"Materials: {v[0]}, broken shaders: {v[1]}"),
            "triangles" => T($"Треугольников: {value}", $"Triangles: {value}"),
            "humanoid" => T("Гуманоидный скелет", "Humanoid skeleton"),
            "height" => T($"Рост: {value} м", $"Height: {value} m"),
            _ => $"{code}: {value}",
        };
    }

    // ---- system check
    public static string SystemCheck => T("Проверка системы", "System check");
    public static string SystemOk => T("Всё готово", "All set");
    public static string SystemProblems(int n) => T($"Нужно внимание: {n}", $"Needs attention: {n}");
    public static string SystemIntro => T("Для конвертации нужны Beat Saber с модом Custom Avatars и Unity нужной версии. Unity работает в фоне, открывать его не придётся.", "Converting needs Beat Saber with the Custom Avatars mod and the right Unity version. Unity runs in the background; you never open it.");
    public static string SystemIntroQuest => T("Для Quest нужен только Unity 2021.3.16f1 с модулем Android: ни Beat Saber, ни BSManager на этом ПК не нужны. Unity работает в фоне, открывать его не придётся.", "For the Quest you only need Unity 2021.3.16f1 with the Android module; Beat Saber and BSManager are not needed on this PC. Unity runs in the background; you never open it.");
    public static string Recheck => T("Проверить снова", "Check again");
    public static string WhichUnity => T("Какая Unity нужна для какой версии", "Which Unity for which version");
    public static string ColGame => T("Beat Saber", "Beat Saber");
    public static string ColGameUnity => T("Игра на Unity", "Game runs on");
    public static string ColCa => T("Custom Avatars", "Custom Avatars");
    public static string ColEditor => T("Нужна Unity", "Build with");
    public static string Supported => T("поддерживается", "supported");
    public static string NotSupported => T("не поддерживается", "not supported");

    public static string ChkGame => T("Beat Saber", "Beat Saber");
    public static string ChkGameOk(string label, string? ver, string? unity) => T($"{label}: версия {ver ?? "?"}, движок Unity {unity ?? "?"}", $"{label}: version {ver ?? "?"}, Unity {unity ?? "?"}");
    public static string ChkGameMissing => T("Не нашёл установленный Beat Saber. Укажи папку игры или поставь игру через BSManager.", "No Beat Saber install found. Pick the game folder or install the game with BSManager.");
    public static string ChkGameTooOld(string? ver) => T($"Beat Saber {ver} слишком старый: этот конвертер рассчитан на Custom Avatars 5.x (Beat Saber 1.30 и новее).", $"Beat Saber {ver} is too old: this converter targets Custom Avatars 5.x (Beat Saber 1.30+).");
    public static string ChkCa => T("Мод Custom Avatars", "Custom Avatars mod");
    public static string ChkCaOk(string v) => T($"Установлен, версия {v}", $"Installed, version {v}");
    public static string ChkCaOther(string v, string want) => T($"Установлена версия {v}, конвертер рассчитан на {want}. Скорее всего заработает, но проверь аватар в игре.", $"Version {v} installed; the converter targets {want}. It will most likely work, but check the avatar in game.");
    public static string ChkCaMissing => T("Мод не установлен в этой копии игры. Открой BSManager → выбери версию игры → «Моды» → отметь Custom Avatars → «Установить».", "The mod isn't installed in this game copy. Open BSManager → pick the game version → Mods → tick Custom Avatars → Install.");
    public static string ChkCaLibs => T("Мод стоит, но нет FinalIK.dll / DynamicBone.dll в папке Libs. Переустанови Custom Avatars через BSManager.", "The mod is there but FinalIK.dll / DynamicBone.dll are missing from Libs. Reinstall Custom Avatars with BSManager.");
    public static string ChkBsm => T("BSManager", "BSManager");
    public static string ChkBsmOk => T("Установлен", "Installed");
    public static string ChkBsmMissing => T("Не установлен. Необязательно, но с ним проще всего поставить нужную версию игры и мод Custom Avatars.", "Not installed. Optional, but it's the easiest way to get the right game version and the Custom Avatars mod.");
    public static string ChkHub => T("Unity Hub", "Unity Hub");
    public static string ChkHubOk => T("Установлен", "Installed");
    public static string ChkHubMissing => T("Не установлен. Через него ставится Unity и бесплатная лицензия.", "Not installed. It installs Unity and the free license.");
    public static string ChkUnity => T($"Unity {Compatibility.UnityVersion}", $"Unity {Compatibility.UnityVersion}");
    public static string ChkUnityOk => T("Установлена", "Installed");
    public static string ChkUnityOther(string v) => T($"Нужной версии нет, будет использована {v}. Лучше поставить {Compatibility.UnityVersion}.", $"Exact version missing; {v} will be used. Installing {Compatibility.UnityVersion} is recommended.");
    public static string ChkUnityMissing => T($"Не установлена. Кнопка ниже сама скачает и тихо поставит Unity Hub, Unity {Compatibility.UnityVersion} и нужные модули (около 3 ГБ на диске, один запрос прав администратора). Останется только войти в бесплатный аккаунт Unity.", $"Not installed. The button below downloads and silently installs Unity Hub, Unity {Compatibility.UnityVersion} and the modules it needs (about 3 GB on disk, one admin prompt). Only signing in to a free Unity account is left to you.");
    public static string ChkWinSupport => T("Модуль Windows Build Support", "Windows Build Support module");
    public static string ChkWinSupportOk => T("Есть", "Present");
    public static string ChkWinSupportMissing => T("Нет модуля «Windows Build Support (Mono)». Добавь его в Unity Hub: Installs → ⚙ у версии → Add modules.", "\"Windows Build Support (Mono)\" is missing. Add it in Unity Hub: Installs → ⚙ next to the version → Add modules.");
    public static string ChkLicense => T("Лицензия Unity", "Unity license");
    public static string ChkLicenseOk => T("Найдена", "Found");
    public static string ChkLicenseMissing => T("Не нашёл лицензию. Открой Unity Hub, войди в аккаунт Unity и добавь бесплатную лицензию Personal.", "No license found. Open Unity Hub, sign in and add a free Personal license.");

    public static string ActPickGame => T("Указать папку игры", "Pick game folder");
    public static string ActInstallBsm => T("Скачать и установить BSManager", "Download and install BSManager");
    public static string ActOpenBsm => T("Открыть BSManager", "Open BSManager");
    public static string ActModPage => T("Страница мода", "Mod page");
    public static string ActInstallHub => T("Скачать и установить Unity Hub", "Download and install Unity Hub");
    public static string ActInstallUnityHub => T("Установить через Unity Hub", "Install with Unity Hub");
    public static string ActInstallUnityDirect => T("Скачать и установить напрямую (2,4 ГБ)", "Download and install directly (2.4 GB)");
    // ---- automatic Unity setup
    public static string SetupTitle => T("Готовлю Unity — всё делается само", "Setting up Unity by itself");
    public static string SetupTitlePermission => T("Нужно разрешение Windows", "Windows needs your permission");
    public static string SetupTitleSignIn => T("Последний шаг: войди в Unity", "Last step: sign in to Unity");
    public static string SetupTitleFailed => T("Установка Unity остановилась", "Unity setup stopped");
    public static string SetupReady => T("Unity готова — можно конвертировать", "Unity is ready, you can convert");
    public static string SetupReadyHint => T("Перетаскивай файлы и жми «Конвертировать». Это окно скоро скроется.", "Drop files and press Convert. This panel hides in a moment.");
    public static string UpdateTitle(string v) => T($"Вышла новая версия SaberBridge {v}", $"SaberBridge {v} is out");
    public static string UpdateText(string current, bool oneClick) => oneClick
        ? T($"У тебя {current}. Обновление скачается с GitHub, программа перезапустится сама.", $"You have {current}. The update downloads from GitHub and the app restarts by itself.")
        : T($"У тебя {current}. Скачай новую версию со страницы релиза.", $"You have {current}. Get the new version from the release page.");
    public static string UpdateNow => T("Обновить", "Update");
    public static string UpdateDownload => T("Скачать", "Download");
    public static string UpdateWhatsNew => T("Что нового", "What's new");
    public static string UpdateSkip => T("Пропустить эту версию", "Skip this version");
    public static string UpdateBusy => T("Обновление станет доступно, когда закончится конвертация или установка Unity.", "Update becomes available once the conversion or the Unity setup is done.");
    public static string UpdateDownloading(string done, string total) => T($"Скачиваю обновление: {done} из {total}", $"Downloading the update: {done} of {total}");
    public static string UpdateBadChecksum => T("Скачанный файл не совпал с контрольной суммой GitHub, он удалён. Попробуй ещё раз.", "The downloaded file does not match GitHub's checksum and was deleted. Try again.");
    public static string UpdateRetrying(int attempt, int of) => T($"GitHub не ответил, пробую ещё раз ({attempt} из {of})…", $"GitHub didn't answer, trying again ({attempt} of {of})…");
    public static string UpdateServerBusy => T("GitHub сейчас не отдаёт файл (сервер перегружен или недоступен). Ничего не изменилось — нажми «Обновить» попозже.", "GitHub isn't serving the file right now (busy or unreachable). Nothing was changed; press Update again later.");
    public static string UpdateFailed(string why) => T($"Не получилось обновиться: {why}", $"Update failed: {why}");
    public static string UpdateCheck => T("Проверить обновления", "Check for updates");
    public static string UpdateChecking => T("Проверяю…", "Checking…");
    public static string UpdateNone => T("У тебя последняя версия.", "You have the latest version.");
    public static string UpdateOffline => T("GitHub не отвечает. Проверь интернет или попробуй позже.", "GitHub can't be reached. Check the connection or try later.");
    public static string AppVersion(string v) => T($"Версия {v}", $"Version {v}");
    public static string SetupWaitStatus => T("Сначала программа подготовит Unity — ход установки в панели сверху.", "Setting up Unity first; progress is in the panel at the top.");
    public static string DiskEditor => T("Unity и модуль Android", "Unity and the Android module");
    public static string DiskHub => T("Unity Hub и Visual C++", "Unity Hub and Visual C++");
    public static string DiskDownloads => T("установщики на время загрузки", "installers while downloading");
    public static string SetupChecking => T("Проверяю, что уже установлено…", "Checking what is already installed…");
    public static string SetupLinux => T("На Linux поставь Unity Hub по инструкции в проверке системы.", "On Linux, install Unity Hub as the system check explains.");
    public static string SetupNoSpace(string drive, double gb) => T($"Не хватает места на диске {drive}: освободи ещё {gb:0.#} ГБ и нажми «Повторить».", $"Not enough space on {drive}: free another {gb:0.#} GB and press Retry.");
    public static string SetupSizing => T("Узнаю у серверов размеры файлов…", "Asking the servers for the file sizes…");
    public static string SetupDownloadingFile(string name, int i, int n, long done, long total) => T(
        $"Скачиваю {name} ({i} из {n}): {Mb(done)} из {Mb(total)} МБ",
        $"Downloading {name} ({i} of {n}): {Mb(done)} of {Mb(total)} MB");
    public static string SetupBytes(long done, long total, double bps, TimeSpan? left) => T(
        $"Всего скачано {Gb(done)} из {Gb(total)} ГБ · {Pct(done, total)} · {Speed(bps)} · осталось {Left(left)}. Если связь оборвётся, продолжу с того же места.",
        $"Downloaded {Gb(done)} of {Gb(total)} GB in total · {Pct(done, total)} · {Speed(bps)} · {Left(left)} left. If the connection drops, it resumes where it stopped.");
    public static string SetupInstallingName(string name, int min, int sec) => T($"Устанавливаю {name}… идёт {min}:{sec:00}", $"Installing {name}… {min}:{sec:00} so far");
    public static string SetupWritten(long done, long total, double bps, TimeSpan? left) => T(
        $"Записано на диск {Gb(done)} из {Gb(total)} ГБ · {Pct(done, total)} · {Speed(bps)} · осталось {Left(left)}",
        $"Written to disk {Gb(done)} of {Gb(total)} GB · {Pct(done, total)} · {Speed(bps)} · {Left(left)} left");
    private static CultureInfo Culture => Ru ? CultureInfo.GetCultureInfo("ru-RU") : CultureInfo.InvariantCulture;
    private static string Mb(long b) => (b / 1048576.0).ToString("N0", Culture);
    private static string Gb(long b) => (b / 1073741824.0).ToString("0.00", Culture);
    private static string Pct(long done, long total) => total > 0 ? $"{100.0 * done / total:0}%" : "…";
    private static string Speed(double bps) => bps > 1 ? T($"{bps / 1048576.0:0.0} МБ/с", $"{bps / 1048576.0:0.0} MB/s") : T("скорость считается", "measuring speed");
    private static string Left(TimeSpan? t) => t is not { } v ? T("— считаю", "— measuring")
        : v.TotalHours >= 1 ? T($"~{(int)v.TotalHours} ч {v.Minutes} мин", $"~{(int)v.TotalHours} h {v.Minutes} min")
        : v.TotalMinutes >= 1 ? T($"~{(int)v.TotalMinutes} мин {v.Seconds} с", $"~{(int)v.TotalMinutes} min {v.Seconds} s")
        : T($"~{v.Seconds} с", $"~{v.Seconds} s");
    public static string SetupDownloading(string name, int i, int n, double doneMb, double? totalMb) => T(
        $"Скачиваю {name} ({i} из {n}): {doneMb:0} из {(totalMb is { } t ? t.ToString("0") : "?")} МБ. Если связь оборвётся — продолжу с того же места.",
        $"Downloading {name} ({i} of {n}): {doneMb:0} of {(totalMb is { } t2 ? t2.ToString("0") : "?")} MB. If the connection drops, it resumes where it stopped.");
    public static string SetupRetryIn(int s) => T($"Связь с сервером Unity прервалась, пробую снова через {s} с…", $"Lost the connection to Unity's server, retrying in {s} s…");
    public static string SetupBlocked => T("Серверы Unity или Microsoft не отвечают из этой сети — похоже на блокировку. Включи VPN: загрузка продолжится сама, с того же места.", "Unity's or Microsoft's servers can't be reached from this network; it looks blocked. Turn on a VPN: the download continues by itself from where it stopped.");
    public static string SetupOffline => T("Нет связи с интернетом. Как только появится — загрузка продолжится сама.", "No internet connection. As soon as it is back, the download continues by itself.");
    public static string SetupDownloadFailed(string why) => T($"Не получилось скачать после нескольких попыток: {why}. Проверь интернет и нажми «Повторить» — скачанное не пропадёт.", $"Could not download after several attempts: {why}. Check the connection and press Retry; what was downloaded is kept.");
    public static string SetupAskPermission => T("Windows спросит разрешение на установку — нажми «Да». Это единственный раз.", "Windows will ask for permission to install: press Yes. This is the only time.");
    public static string SetupPermissionDeclined => T("Установка не началась: Windows не получила разрешение.", "The install did not start: Windows did not get permission.");
    public static string SetupPermissionWhy => T("Установщики Unity требуют прав администратора. Нажми «Разрешить» и выбери «Да».", "Unity's installers need admin rights. Press Allow and choose Yes.");
    public static string SetupInstalling(string name, int min, int sec) => T($"Устанавливаю {name} в фоне… {min}:{sec:00} (на HDD это может занять 10–20 минут)", $"Installing {name} in the background… {min}:{sec:00} (on a hard drive this can take 10–20 minutes)");
    public static string SetupInstallerFailed(string name, string code) => T($"Установщик «{name}» завершился с ошибкой ({code}). Нажми «Повторить»; подробности — в журнале.", $"The {name} installer failed ({code}). Press Retry; details are in the log.");
    public static string SetupVerifying => T("Проверяю установку…", "Checking the install…");
    public static string SetupNoEditor(string path) => T($"После установки не нашёл Unity в {path}.", $"Unity is not in {path} after the install.");
    public static string SetupNoModule(string name) => T($"Не установился модуль {name} для Unity.", $"Unity's {name} module did not install.");
    public static string SetupHubNotFound => T("Установщик Unity Hub сказал «готово», но программа не нашла, куда он встал (так бывает, если Hub раньше ставили в другую папку). Если Hub у тебя открывается — нажми «Указать Unity Hub…» и выбери «Unity Hub.exe» (правый клик по ярлыку Hub → «Расположение файла»).", "Unity Hub's installer said it was done, but the app cannot find where it went (this happens when Hub was installed to another folder before). If Hub opens for you, press \"Find Unity Hub…\" and pick \"Unity Hub.exe\" (right-click the Hub shortcut → Open file location).");
    public static string ActPickHub => T("Указать Unity Hub…", "Find Unity Hub…");
    public static string SetupScriptBlocked => T("Установка не запустилась: окно прав администратора закрылось, или антивирус / политика Windows не дали запустить PowerShell. Нажми «Повторить» и разреши запуск; подробности — в журнале.", "The install did not start: the admin prompt closed, or an antivirus / Windows policy did not let PowerShell run. Press Retry and allow it; details are in the log.");
    public static string SetupNoHub => T("Не установился Unity Hub (он нужен для входа и лицензии).", "Unity Hub did not install (it is needed to sign in for the license).");
    public static string SetupPreparing => T("Первый запуск Unity: готовлю проект…", "First Unity start: preparing the project…");
    public static string SetupPreparingTime(int min, int sec) => T($"Первый запуск Unity: готовлю проект… {min}:{sec:00} (только один раз, обычно 3–10 минут)", $"First Unity start: preparing the project… {min}:{sec:00} (only once, usually 3–10 minutes)");
    public static string ActDetails => T("Подробнее", "Details");
    public static string GbShort => T("ГБ", "GB");
    public static string DiskShort => T("мало места", "too little");
    public static string GuideWindowTitle => T("Установка Unity", "Unity setup");
    public static string GuideIntro => T(
        "Аватары для ПК и файлы для Quest собирает сама Unity — так же, как это делают авторы модов. Программа ставит бесплатную версию Unity сама: скачивает, проверяет, устанавливает и готовит проект. От тебя нужно только два действия, они отмечены ниже: разрешить установку в окне Windows и один раз войти в аккаунт Unity.",
        "PC avatars and Quest files are built by Unity itself, the same way mod authors build them. The app installs the free Unity by itself: downloads, checks, installs and prepares the project. Only two things need you, marked below: allowing the install in a Windows prompt and signing in to a Unity account once.");
    public static string GuideAccountTitle => T("Понадобится бесплатный аккаунт Unity", "You will need a free Unity account");
    public static string GuideAccountBody => T(
        "Unity бесплатна, но её лицензия выдаётся на аккаунт Unity, и в конце установки нужно будет в него войти. Если аккаунта нет — заведи его заранее, пока идёт скачивание: это бесплатно, хватит почты или входа через Google. Тогда в конце останется только нажать «Sign in».",
        "Unity is free, but its license is issued to a Unity account, and at the end of the install you sign in to it. No account yet? Create one now while the download runs: it's free, an email or a Google sign-in is enough. Then at the end you just press Sign in.");
    public static string ActCreateAccount => T("Создать аккаунт", "Create account");
    public static string SetupAddPersonalLicense => T("Вход в Unity Hub есть, но лицензии ещё нет — добавь бесплатную лицензию Personal.", "Signed in to Unity Hub, but there is no license yet: add the free Personal license.");
    public static string SetupAddPersonalLicenseHow => T("В Unity Hub: шестерёнка (Preferences) → Licenses → Add → Get a free personal license. Программа проверит снова сама, как только лицензия появится.", "In Unity Hub: the gear (Preferences) → Licenses → Add → Get a free personal license. The app checks again by itself as soon as it appears.");
    public static string SetupCheckingLicense => T("Проверяю лицензию: запускаю Unity…", "Checking the license: starting Unity…");
    public static string SetupUnityFailed(int code) => T($"Unity завершилась с ошибкой (код {code}). Подробности — в журнале.", $"Unity stopped with an error (code {code}). Details are in the log.");
    public static string GuideSpaceTitle => T("Проверка места на диске", "Checking disk space");
    public static string GuideSpaceBody => T("Unity занимает около 7 ГБ, плюс ~3 ГБ установщиков на время загрузки (потом они удаляются). Если на системном диске тесно, Unity встанет на другой диск, где места хватает — таблица ниже.", "Unity takes about 7 GB, plus ~3 GB of installers while downloading (deleted afterwards). If the system drive is tight, Unity goes to another drive with room; see the table below.");
    public static string GuideDownloadTitle => T("Скачивание (~3 ГБ)", "Download (~3 GB)");
    public static string GuideDownloadBody => T("Только с официальных серверов Unity и Microsoft. У каждого файла проверяется цифровая подпись — подделанный файл удаляется и не запускается. Обрыв связи не страшен: загрузка продолжится с того же места.", "Only from Unity's and Microsoft's own servers. Every file's digital signature is checked; a tampered file is deleted and never run. A dropped connection is fine: the download resumes where it stopped.");
    public static string GuidePermissionTitle => T("Разрешение Windows — нужно нажать «Да»", "Windows permission — press Yes");
    public static string GuidePermissionBody => T("Установщики Unity пишут в Program Files, а это Windows разрешает только с правами администратора — обойти нельзя, так устроены сами установщики. Windows покажет окно один раз на всю установку.", "Unity's installers write to Program Files, which Windows only allows with admin rights; there is no way around it, the installers are built that way. Windows asks once for the whole install.");
    public static string GuideInstallTitle => T("Установка (обычно 5–20 минут)", "Install (usually 5–20 minutes)");
    public static string GuideInstallBody => T("Идёт в фоне, видно сколько гигабайт уже записано. В конце установщик Unity просит у Windows компонент .NET Framework 3.5; если Windows Update недоступен, этот шаг пропустится — Unity работает и без него.", "Runs in the background, showing how many gigabytes are written. At the end Unity's installer asks Windows for .NET Framework 3.5; if Windows Update can't be reached, that step is skipped, Unity works without it.");
    public static string GuideSignInTitle => T("Вход в Unity — один раз", "Unity sign-in — once");
    public static string GuideSignInBody => T("Бесплатная лицензия Unity Personal привязана к аккаунту Unity, поэтому войти нужно самому: программа откроет Unity Hub. Если Hub не добавит лицензию сам — шестерёнка → Licenses → Add → Get a free personal license. Программа проверит лицензию, запустив Unity.", "The free Unity Personal license belongs to a Unity account, so you sign in yourself: the app opens Unity Hub. If Hub does not add the license by itself: the gear → Licenses → Add → Get a free personal license. The app checks the license by starting Unity.");
    public static string GuidePrepareTitle => T("Подготовка проекта", "Preparing the project");
    public static string GuidePrepareBody => T("Первый запуск Unity готовит рабочий проект программы — один раз, обычно 1–10 минут. Потом можно конвертировать.", "Unity's first start prepares the app's project, once, usually 1–10 minutes. Then you can convert.");
    public static string GuideDisksTitle => T("Место на дисках", "Disk space");
    public static string GuideEditorDir(string dir) => T($"Unity будет установлена в: {dir}", $"Unity goes to: {dir}");
    public static string GuideNothingToInstall => T("Unity и всё нужное уже установлены — дополнительное место не требуется.", "Unity and everything it needs is already installed; no extra space is needed.");
    public static string GuideDisk => T("Диск", "Drive");
    public static string GuideWhat => T("Что ляжет", "What goes there");
    public static string GuideNeed => T("Нужно", "Needed");
    public static string GuideFree => T("Свободно", "Free");
    public static string GuideUacTitle => T("Как выглядит окно Windows", "What the Windows prompt looks like");
    public static string GuideUacBody => T("В окне будет написано «Windows PowerShell» — через него программа запускает установщики Unity и Microsoft одним разрешением. Нажми «Да». Если нажать «Нет», ничего не сломается: в панели появится кнопка «Разрешить», чтобы спросить снова.", "The prompt says \"Windows PowerShell\": the app runs Unity's and Microsoft's installers through it with one permission. Press Yes. Pressing No breaks nothing: the panel shows an Allow button to ask again.");
    public static string UacHeader => T("Контроль учётных записей", "User Account Control");
    public static string UacQuestion => T("Разрешить этому приложению вносить изменения на вашем устройстве?", "Do you want to allow this app to make changes to your device?");
    public static string UacPublisher => T("Проверенный издатель: Microsoft Windows", "Verified publisher: Microsoft Windows");
    public static string UacYes => T("Да", "Yes");
    public static string UacNo => T("Нет", "No");
    public static string GuideHubTitle => T("Что нажать в Unity Hub", "What to press in Unity Hub");
    public static string GuideHubBody => T(
        "1. В открывшемся Unity Hub нажми «Sign in» — откроется браузер.\n2. Войди в аккаунт Unity или создай его бесплатно (можно через Google).\n3. Обязательно добавь лицензию — сам Hub её не выдаёт: шестерёнка (Preferences) → «Licenses» → «Add» → «Get a free personal license».\n4. Всё — программа сама проверит лицензию, запустив Unity. Unity Hub можно закрыть.",
        "1. In Unity Hub press Sign in; a browser opens.\n2. Sign in to a Unity account or create one for free (Google works too).\n3. Add the license, Hub does not do it by itself: the gear (Preferences) → Licenses → Add → Get a free personal license.\n4. Done: the app checks the license by starting Unity. You can close Unity Hub.");
    public static string GuideTroubleTitle => T("Если что-то пошло не так", "If something goes wrong");
    public static string GuideTroubleBody => T(
        "• Нажал «Нет» в окне Windows — нажми «Разрешить» в панели программы.\n• Пишет, что сервер не отвечает или «похоже на блокировку» — включи VPN, загрузка продолжится сама.\n• Не хватает места — освободи место на указанном диске и нажми «Повторить».\n• Ошибка установщика — нажми «Повторить»; подробности в «Журнале» (его можно приложить к сообщению об ошибке).\n• Закрыл программу посреди загрузки — при следующем запуске она продолжит с того же места.",
        "• Pressed No in the Windows prompt: press Allow in the app's panel.\n• It says the server doesn't answer or \"looks blocked\": turn on a VPN, the download carries on by itself.\n• Not enough space: free space on the drive shown and press Retry.\n• An installer error: press Retry; details are in the Log (attach it to a bug report).\n• Closed the app mid-download: on the next start it resumes where it stopped.");
    public static string ActSkip => T("Пропустить", "Skip");
    public static string SetupNetFx(int? pct, int min, int sec) => T(
        $"Windows ставит .NET Framework 3.5 для установщика Unity: загружено {(pct is { } p ? p + "%" : "…")}, идёт {min}:{sec:00}",
        $"Windows is adding .NET Framework 3.5 for Unity's installer: {(pct is { } p2 ? p2 + "%" : "…")} downloaded, {min}:{sec:00} so far");
    public static string SetupNetFxWhy => T("Этот компонент Windows качает через Windows Update. Сама Unity работает и без него, поэтому если загрузка встанет — шаг пропустится сам.", "Windows downloads this through Windows Update. Unity works without it, so if the download stalls, the step is skipped by itself.");
    public static string SetupNetFxSlow => T("Windows Update не отдаёт файлы — похоже, он заблокирован в этой сети. Через 3 минуты без движения шаг пропустится сам, или нажми «Пропустить».", "Windows Update is not sending anything; it may be blocked on this network. After 3 minutes without progress the step is skipped by itself, or press Skip.");
    public static string SetupNetFxSkipping => T("Пропускаю .NET Framework 3.5 — Unity без него работает. Установка продолжается.", "Skipping .NET Framework 3.5; Unity works without it. The install carries on.");
    public static string ActAllow => T("Разрешить", "Allow");
    public static string ActRetry => T("Повторить", "Retry");
    public static string ActLater => T("Позже", "Later");
    public static string ActLog => T("Журнал", "Log");
    public static string ActInstallAll => T("Установить всё автоматически", "Install everything automatically");
    public static string ActSignIn => T("Войти в Unity Hub", "Sign in to Unity Hub");
    public static string WaitingForLicense => T("Войди в аккаунт Unity в открывшемся Unity Hub (бесплатно). Если Hub попросит лицензию — выбери «Personal». Программа сама заметит лицензию и продолжит.", "Sign in to your Unity account in the Unity Hub window (free). If Hub asks for a license, pick Personal. The app notices the license by itself and carries on.");
    public static string LicenseTimeout => T("Лицензия так и не появилась. Войди в Unity Hub и нажми «Проверить снова».", "No license appeared. Sign in to Unity Hub, then press Check again.");
    public static string AdminDeclined => T("Установка отменена: Windows не получила разрешение администратора.", "Install cancelled: Windows did not get admin permission.");
    public static string ActOpenHub => T("Открыть Unity Hub", "Open Unity Hub");
    public static string ActInstructions => T("Инструкция", "Instructions");
    public static string Downloading(double doneMb, double? totalMb) => totalMb is { } t ? T($"Скачиваю {doneMb:0} из {t:0} МБ", $"Downloading {doneMb:0} of {t:0} MB") : T($"Скачиваю {doneMb:0} МБ", $"Downloading {doneMb:0} MB");
    public static string Installing => T("Устанавливаю… (Windows может спросить разрешение)", "Installing… (Windows may ask for permission)");
    public static string WaitingForHub => T("Unity Hub открыт: подтверди установку там, потом нажми «Проверить снова».", "Unity Hub is open: confirm the install there, then press Check again.");
    public static string ActionFailed(string msg) => T($"Не получилось: {msg}", $"Failed: {msg}");

    // ---- Unity while it starts, its dialogs
    public static string UnityActivity(string phase, TimeSpan t)
    {
        string what = phase switch
        {
            "license" => T("проверяет лицензию", "checking the license"),
            "engine" => T("загружается", "loading"),
            "import" => T("проверяет файлы проекта", "checking the project files"),
            "scripts" => T("компилирует скрипты", "compiling scripts"),
            "shaders" => T("готовит шейдеры", "preparing shaders"),
            "method" => T("запускает конвертер", "starting the converter"),
            _ => T("запускается", "starting"),
        };
        return T($"Unity {what} · {(int)t.TotalMinutes}:{t.Seconds:00} (в фоне, окно не появится)", $"Unity is {what} · {(int)t.TotalMinutes}:{t.Seconds:00} (in the background, no window)");
    }

    public static string UnityDialog(string text) => T(
        $"Unity остановилась на своём окне с вопросом, ответить на который некому: «{text}». Подробности — в журнале (Настройки → Журналы).",
        $"Unity stopped at a dialog of its own that nobody can answer: \"{text}\". Details are in the log (Settings → Logs).");

    // ---- logs
    public static string LogsTitle => T("Журналы", "Logs");
    public static string LogsHint => T("Программа записывает каждый шаг: запуски Unity, установку, ошибки. Если что-то зависло или сломалось — нажми «Собрать для отправки» и пришли получившийся zip.", "The app writes down every step: Unity runs, the setup, errors. If something hangs or breaks, press \"Collect for sending\" and send the zip.");
    public static string ActOpenLogs => T("Открыть папку", "Open folder");
    public static string ActCollectLogs => T("Собрать для отправки", "Collect for sending");
    public static string LogsCollected(string name) => T($"Готово: {name} на рабочем столе", $"Done: {name} on the desktop");

    // ---- first-run window
    public static string FirstRunTitle => T("Первоначальная настройка", "First-time setup");
    public static string ActFirstRun => T("Первоначальная настройка…", "First-time setup…");
    public static string FirstRunIntro => T(
        "Здесь по пунктам: что нужно программе, зачем, где оно стоит и что уже готово. Почти всё ставится само — от тебя нужны только разрешение Windows и один вход в аккаунт Unity. Окно можно открыть снова: Настройки → «Первоначальная настройка».",
        "Item by item: what the app needs, why, where it is and what is ready. Almost everything installs by itself; only the Windows permission and one Unity sign-in need you. Open this window again any time: Settings → First-time setup.");
    public static string ChecklistTitle => T("Что нужно и что уже есть", "What is needed and what is there");
    public static string ActRecheck => T("Проверить снова", "Check again");
    public static string CheckOk => T("готово", "ready");
    public static string CheckMissing => T("нет", "missing");
    public static string CheckOptional => T("не обязательно", "optional");
    public static string CheckAuto => T("поставится само", "installs by itself");
    public static string CheckWhere(string where) => T($"Где: {where}", $"Where: {where}");
    public static string NotFound => T("не найдено", "not found");
    public static string LicenseHere => T("файл лицензии есть на этом ПК", "a license file is on this PC");
    public static string SetupNow => T("Сейчас", "Right now");

    public static string ItemGame => T("Beat Saber для ПК", "Beat Saber for PC");
    public static string ItemGameWhy => T(
        "Нужна только для аватаров ПК и для .avatar → .vrm: из папки игры берутся библиотеки FinalIK и DynamicBone (они платные, программа не имеет права их раздавать). Для сейберов и нот на Quest игра не нужна.",
        "Only for PC avatars and .avatar → .vrm: the FinalIK and DynamicBone libraries are taken from the game folder (they are paid, the app may not ship them). Quest sabers and notes don't need the game.");
    public static string ItemUnity => T($"Unity {Compatibility.UnityVersion}", $"Unity {Compatibility.UnityVersion}");
    public static string ItemUnityWhy => T(
        "Файлы .avatar, .whacker и .cyoob — это пакеты Unity. Собрать их может только сама Unity той же версии, на которой сделана игра; поэтому ставится именно эта версия, а не новейшая. Программа ставит её сама, около 7 ГБ.",
        "The .avatar, .whacker and .cyoob files are Unity packages. Only Unity itself, the same version the game is made with, can build them; that's why this exact version, not the newest. The app installs it by itself, about 7 GB.");
    public static string ItemAndroid => T("Модуль Android для Unity", "Unity's Android module");
    public static string ItemAndroidWhy => T("Quest работает на Android: без этого модуля Unity не соберёт файлы для Quest. Ставится вместе с Unity.", "The Quest runs Android: without this module Unity can't build Quest files. Installed together with Unity.");
    public static string ItemVc => T("Microsoft Visual C++ Redistributable", "Microsoft Visual C++ Redistributable");
    public static string ItemVcWhy => T("Системные библиотеки Microsoft, без которых Unity не запускается. Ставится сама, с сайта Microsoft.", "Microsoft's system libraries Unity can't start without. Installs by itself, from Microsoft.");
    public static string ItemHub => T("Unity Hub", "Unity Hub");
    public static string ItemHubWhy => T("Нужен только для того, чтобы войти в аккаунт Unity и получить лицензию. Сама конвертация идёт без него; когда лицензия уже есть, Hub не обязателен.", "Only to sign in to a Unity account and get the license. Converting itself works without it; once there is a license, Hub is optional.");
    public static string ItemLicense => T("Бесплатная лицензия Unity Personal", "Free Unity Personal license");
    public static string ItemLicenseWhy => T(
        "Зачем лицензия: редактор Unity вообще не запускается без лицензии — так устроена сама Unity, это не требование программы. Personal бесплатна для личного использования, выдаётся на твой аккаунт Unity и хранится на этом ПК. Пароль вводишь только ты в Unity Hub — программа его не видит и никуда не отправляет. Как: Unity Hub → «Sign in» → шестерёнка → Licenses → Add → «Get a free personal license». Нужно один раз.",
        "Why a license: the Unity editor won't start without one at all; that's how Unity works, not something the app asks for. Personal is free for personal use, issued to your Unity account and kept on this PC. Only you type the password, in Unity Hub; the app never sees or sends it. How: Unity Hub → Sign in → the gear → Licenses → Add → Get a free personal license. Once.");
    public static string ItemQuestProject => T("Проект для Quest подготовлен", "Quest project prepared");
    public static string ItemQuestProjectWhy => T("Первый запуск Unity импортирует рабочий проект программы: 1–10 минут, один раз. Потом каждый запуск занимает около минуты. Unity работает в фоне — её окно не появляется.", "Unity's first start imports the app's project: 1–10 minutes, once. After that each start takes about a minute. Unity runs in the background, its window doesn't appear.");
    public static string ItemPcProject => T("Проект для ПК подготовлен", "PC project prepared");
    public static string ItemPcProjectWhy => T("То же для аватаров ПК и .avatar → .vrm: готовится при первой такой конвертации, 5–10 минут, один раз.", "The same for PC avatars and .avatar → .vrm: prepared on the first such conversion, 5–10 minutes, once.");
    public static string FirstRunStepsTitle => T("Как идёт установка", "How the setup goes");
}
