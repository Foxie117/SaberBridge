using System.Globalization;
using BSAvatarConverter.Core;

namespace BSAvatarConverter;

/// <summary>UI strings: Russian when the system language is Russian, English otherwise.</summary>
public static class L
{
    public static bool Ru { get; } = (Core.Settings.Load().Language ?? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName) == "ru";

    private static string T(string ru, string en) => Ru ? ru : en;

    public static string Subtitle => T("VRM · VRChat · MMD  →  аватары для Beat Saber", "VRM · VRChat · MMD  →  Beat Saber avatars");
    public static string DropTitle => T("Перетащи модели сюда", "Drop your models here");
    public static string DropHint => T(".vrm  ·  .unitypackage  ·  .pmx  ·  .zip  ·  папки", ".vrm  ·  .unitypackage  ·  .pmx  ·  .zip  ·  folders");
    public static string Browse => T("Выбрать файлы", "Choose files");
    public static string Queue => T("Очередь", "Queue");
    public static string Clear => T("Очистить", "Clear");
    public static string Results => T("Готовые аватары", "Your avatars");
    public static string ResultsEmpty => T("Здесь появятся аватары с обложками.\nКонвертация каждой модели занимает около минуты.", "Converted avatars show up here with their covers.\nEach model takes about a minute.");
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
        _ => "",
    };

    public static string Cancelled => T("Отменено", "Cancelled");
    public static string NothingToDo => T("Добавь модели в очередь", "Add some models first");
}
