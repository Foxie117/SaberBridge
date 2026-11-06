using System.Globalization;
using SaberBridge.Core;

namespace SaberBridge;

/// <summary>UI strings: Russian when the system language is Russian, English otherwise.</summary>
public static class L
{
    public static bool Ru { get; } = (Core.Settings.Load().Language ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) == "ru";

    private static string T(string ru, string en) => Ru ? ru : en;

    public static string Subtitle => T("Конвертер для Beat Saber: аватары для ПК и сейберы для Quest", "Beat Saber converter: PC avatars and Quest sabers");
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

    public static string Ready => T("Готово к работе", "Ready");
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
    public static string TabPc => T("ПК · аватары", "PC · avatars");
    public static string TabQuest => T("Quest · сейберы", "Quest · sabers");
    public static string Sabers => T("Сейберы для Quest", "Quest sabers");
    public static string SaberDropTitle => T("Перетащи сейберы .saber сюда", "Drop .saber files here");
    public static string SaberDropHint => T(".saber (ПК)  →  .whacker (Quest, Qosmetics)", ".saber (PC)  →  .whacker (Quest, Qosmetics)");
    public static string SabersEmpty => T("Добавь сейберы с ПК (.saber). Готовые .whacker будут лежать в папке ниже — оттуда их ставишь на Quest через Mods Before Friday.", "Add PC sabers (.saber). Finished .whacker files land in the folder below; install them on the Quest with Mods Before Friday.");
    public static string QuestFolder => T("Папка для Quest", "Quest folder");
    public static string SaberConverted => T("сконвертирован", "converted");
    public static string SaberNotConverted => T("не сконвертирован", "not converted");
    public static string SaberOutputMissing => T("нет сконвертированного файла", "converted file missing");
    public static string SaberSourceMissing(string path) => T($"Оригинал пропал: {path}", $"Original is gone: {path}");
    public static string SaberSummary(int all, int done, int missing) => T($"Всего {all} · готово {done}" + (missing > 0 ? $" · пропало {missing}" : ""), $"{all} total · {done} done" + (missing > 0 ? $" · {missing} missing" : ""));
    public static string SaberEngineNote => T("Оригиналы .saber не изменяются и не перемещаются.", "Original .saber files are never changed or moved.");
    public static string SaberNeedsUnity => T("Для конвертации нужен модуль Android Build Support для Unity 2021.3.16f1 (Unity Hub → Installs → ⚙ → Add modules). Оригиналы .saber не изменяются.", "Converting needs the Android Build Support module for Unity 2021.3.16f1 (Unity Hub → Installs → ⚙ → Add modules). Original .saber files are never changed.");
    public static string SaberReady => T("Готово к конвертации. Оригиналы .saber не изменяются и не перемещаются.", "Ready to convert. Original .saber files are never changed or moved.");
    public static string SaberFirstRun => T("Первый запуск: Unity готовит проект для Quest, это несколько минут.", "First run: Unity is preparing the Quest project, this takes a few minutes.");
    public static string SaberConverting(string what) => T($"Конвертирую {what}", $"Converting {what}");
    public static string SaberFinished(int ok, int failed) => T($"Готово: {ok}" + (failed > 0 ? $", ошибок: {failed}" : "") + ". Файлы .whacker лежат в папке для Quest.", $"Done: {ok}" + (failed > 0 ? $", {failed} failed" : "") + ". The .whacker files are in the Quest folder.");
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
    public static string ChkUnityMissing => T($"Не установлена. Её требует Custom Avatars 5.x для сборки аватаров (около 3 ГБ на диске).", $"Not installed. Custom Avatars 5.x needs it to build avatars (about 3 GB on disk).");
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
    public static string ActOpenHub => T("Открыть Unity Hub", "Open Unity Hub");
    public static string ActInstructions => T("Инструкция", "Instructions");
    public static string Downloading(double doneMb, double? totalMb) => totalMb is { } t ? T($"Скачиваю {doneMb:0} из {t:0} МБ", $"Downloading {doneMb:0} of {t:0} MB") : T($"Скачиваю {doneMb:0} МБ", $"Downloading {doneMb:0} MB");
    public static string Installing => T("Устанавливаю… (Windows может спросить разрешение)", "Installing… (Windows may ask for permission)");
    public static string WaitingForHub => T("Unity Hub открыт: подтверди установку там, потом нажми «Проверить снова».", "Unity Hub is open: confirm the install there, then press Check again.");
    public static string ActionFailed(string msg) => T($"Не получилось: {msg}", $"Failed: {msg}");
}
