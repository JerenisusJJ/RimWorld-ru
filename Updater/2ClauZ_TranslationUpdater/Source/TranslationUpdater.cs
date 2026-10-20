// 2ClauZ Translation Updater — checks GitHub for translation updates
// Harmony patch on MainMenuDrawer to show update button
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace TranslationUpdater
{
    public class UpdaterSettings : ModSettings
    {
        public string repoUrl = "https://raw.githubusercontent.com/JerenisusJJ/RimWorld-ru/master";
        public string lastVersion = "";
        public bool autoSwitch = true;
        public override void ExposeData()
        {
            Scribe_Values.Look(ref autoSwitch, "autoSwitch", true);
        }
    }

    public class UpdaterMod : Mod
    {
        public static UpdaterSettings Settings;
        public UpdaterMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<UpdaterSettings>();
        }
        public override string SettingsCategory() { return "2ClauZ Translation Updater"; }
        public override void DoSettingsWindowContents(Rect inRect)
        {
            var l = new Listing_Standard();
            l.Begin(inRect);
            l.CheckboxLabeled("Автоматически переключать язык на перевод 2ClauZ после проверки/обновления", ref Settings.autoSwitch);
            l.GapLine();
            l.Label("Авторы");
            l.Label("База — официальный русский перевод RimWorld и DLC (github.com/Ludeon/RimWorld-ru). Переводчики: "
                + "Андрей 'Elevator89' Лёмин, Дмитрий 'Dimonasdf' Романов, Евгения 'EcherArt' Артамонова, Николай 'Reiquard' Володин, "
                + "AcDie, Arex-rus, Dandi, Humort, kamadz и другие участники сообщества.");
            l.Label("2ClauZ — дополнения поверх базы: доперевод пропусков, унификация терминов, перевод модов и этот апдейтер. "
                + "Часть дополнений сделана с помощью нейросети и проверена в игре.");
            l.Label("Моды, их авторы и происхождение каждого перевода — в CREDITS.md репозитория.");
            if (l.ButtonText("Открыть CREDITS.md"))
                Application.OpenURL("https://github.com/JerenisusJJ/RimWorld-ru/blob/master/CREDITS.md");
            l.End();
        }
    }

    [StaticConstructorOnStartup]
    public static class Init
    {
        static Init()
        {
            var h = new Harmony("2clauz.translationupdater");
            h.PatchAll(Assembly.GetExecutingAssembly());
            MarksDefaultPreset.TryPatch(h);
            Log.Message("[2ClauZ Updater] initialized");
        }
    }

    // Useful Marks: в новой игре мод ставит зашитый набор меток и пресеты не смотрит.
    // Подставляем вместо него пресет владельца (по имени). Мод не обязателен — нет его, патча нет.
    public static class MarksDefaultPreset
    {
        public const string PresetName = "1";
        static Type compType, pmType;

        public static void TryPatch(Harmony h)
        {
            try
            {
                compType = AccessTools.TypeByName("UsefulMarks.PawnLabelCustomColors_WorldComponent");
                pmType = AccessTools.TypeByName("UsefulMarks.PresetManager");
                if (compType == null || pmType == null) return;
                MethodInfo target = AccessTools.Method(compType, "EnsureHasAnyMarkerSettings");
                if (target == null) return;
                h.Patch(target, prefix: new HarmonyMethod(typeof(MarksDefaultPreset), "Prefix"));
                // уже идущие игры: кнопки «Добавить/Заменить» в моде не работают — применяем сами,
                // один раз на мир (пока пресет к нему ни разу не привязывали)
                MethodInfo fin = AccessTools.Method(typeof(Game), "FinalizeInit");
                if (fin != null) h.Patch(fin, postfix: new HarmonyMethod(typeof(MarksDefaultPreset), "GamePostfix"));
                // старый класс навыкового условия (MarkerConditionSkillLevel) из пресетов не числится в таблице типов мода —
                // GetTypeIndex падает, и кнопки «Добавить/Заменить» в редакторе пресетов молча не работают
                Type skillType = AccessTools.TypeByName("UsefulMarks.MarkerConditionSkillLevel");
                MethodInfo gti = skillType == null ? null : AccessTools.Method(skillType, "GetTypeIndex");
                if (gti != null) h.Patch(gti, prefix: new HarmonyMethod(typeof(MarksDefaultPreset), "SkillTypeIndexPrefix"));
                Log.Message("[2ClauZ Updater] Useful Marks: new games get preset \"" + PresetName + "\"");
            }
            catch (Exception e) { Log.Warning("[2ClauZ Updater] Useful Marks patch failed: " + e.Message); }
        }

        static object FindPreset()
        {
            object pm = AccessTools.Field(pmType, "instance").GetValue(null);
            var presets = AccessTools.Field(pmType, "presets").GetValue(pm) as System.Collections.IList;
            if (presets == null || presets.Count == 0)
            {
                AccessTools.Method(pmType, "Load").Invoke(pm, null);
                presets = AccessTools.Field(pmType, "presets").GetValue(pm) as System.Collections.IList;
            }
            if (presets != null)
                foreach (object p in presets)
                    if (p != null && (AccessTools.Field(p.GetType(), "name").GetValue(p) as string) == PresetName) return p;
            return null;
        }

        static void GamePostfix()
        {
            try
            {
                object comp = AccessTools.Field(compType, "instance").GetValue(null);
                if (comp == null) return;
                string last = AccessTools.Field(compType, "lastLinkedPresetName").GetValue(comp) as string;
                if (!string.IsNullOrEmpty(last)) return; // к миру уже привязывали пресет — не трогаем выбор игрока
                object preset = FindPreset();
                if (preset == null) return;
                var markersF = AccessTools.Field(compType, "UserMarkers");
                if (markersF.GetValue(comp) == null) markersF.SetValue(comp, Activator.CreateInstance(markersF.FieldType));
                object pmarkers = AccessTools.Field(preset.GetType(), "markers").GetValue(preset);
                string data = (string)AccessTools.Method(compType, "SerializeMarkers").Invoke(null, new object[] { pmarkers });
                AccessTools.Method(compType, "ApplyPresetData").Invoke(comp, new object[] { data, true });
                AccessTools.Method(compType, "Link").Invoke(comp, new object[] { preset });
                AccessTools.Field(compType, "Initialized").SetValue(comp, true);
                var markers = markersF.GetValue(comp) as System.Collections.IList;
                Log.Message("[2ClauZ Updater] Useful Marks: applied preset \"" + PresetName + "\" to loaded game (" + (markers == null ? 0 : markers.Count) + " marks)");
            }
            catch (Exception e) { Log.Warning("[2ClauZ Updater] Useful Marks apply to loaded game failed: " + e); }
        }

        // как у Reworked: индекс первой навыковой строки таблицы + номер навыка
        static bool SkillTypeIndexPrefix(object __instance, ref int __result)
        {
            Type reworked = AccessTools.TypeByName("UsefulMarks.MarkerConditionSkillLevelReworked");
            if (reworked == null || __instance.GetType() == reworked) return true;
            Type assets = AccessTools.TypeByName("UsefulMarks.Assets");
            int baseIdx = (int)AccessTools.Field(assets, "skillIndexNodes").GetValue(null);
            __result = baseIdx + (int)AccessTools.Field(__instance.GetType(), "SkillID").GetValue(__instance);
            return false;
        }

        // false — оригинал (зашитые метки) пропускаем
        static bool Prefix(object __instance)
        {
            try
            {
                var markersF = AccessTools.Field(compType, "UserMarkers");
                var initF = AccessTools.Field(compType, "Initialized");
                var markers = markersF.GetValue(__instance) as System.Collections.IList;
                if ((bool)initF.GetValue(__instance) || (markers != null && markers.Count > 0)) return true;
                // во время чтения/записи сейва файл пресетов открывать нельзя — Scribe один; доделаем при следующем вызове
                if (Scribe.mode != LoadSaveMode.Inactive)
                {
                    if (markers == null) markersF.SetValue(__instance, Activator.CreateInstance(markersF.FieldType));
                    return false;
                }
                object preset = FindPreset();
                if (preset == null) return true;
                object pmarkers = AccessTools.Field(preset.GetType(), "markers").GetValue(preset);
                if (markers == null) markersF.SetValue(__instance, Activator.CreateInstance(markersF.FieldType));
                string data = (string)AccessTools.Method(compType, "SerializeMarkers").Invoke(null, new object[] { pmarkers });
                AccessTools.Method(compType, "ApplyPresetData").Invoke(__instance, new object[] { data, true });
                AccessTools.Method(compType, "Link").Invoke(__instance, new object[] { preset });
                markers = markersF.GetValue(__instance) as System.Collections.IList;
                if (markers == null || markers.Count == 0) return true;
                initF.SetValue(__instance, true);
                Log.Message("[2ClauZ Updater] Useful Marks: applied preset \"" + PresetName + "\" (" + markers.Count + " marks)");
                return false;
            }
            catch (Exception e)
            {
                Log.Warning("[2ClauZ Updater] Useful Marks preset apply failed: " + e);
                return true;
            }
        }
    }

    public class UpdaterState
    {
        public static string localVersion = "";
        public static string remoteVersion = "";
        public static bool checkedOnce = false;
        public static bool updateAvailable = false;
        public static bool downloading = false;
        public static bool pendingRestartDialog = false;
        public static string statusMsg = "";
        public static float statusTime = 0f;
        public static Thread workThread = null;
        public static float busySince = -1f;
        public static bool wantAutoSwitch = false;
        public static bool autoChecked = false;
        // чего не хватает на диске (заполняет проверка в фоне); пусто — файлы в порядке
        public static string missingFiles = "";

        public static bool AutoSwitchEnabled
        {
            get { return UpdaterMod.Settings == null || UpdaterMod.Settings.autoSwitch; }
        }

        // имя папки нашего языка (однозначно, не пересекается с офиц. "Russian (Русский)")
        public const string OurFolder = "Russian (2clauz)";

        public static LoadedLanguage FindOurLanguage()
        {
            foreach (var l in LanguageDatabase.AllLoadedLanguages)
                if (l.folderName == OurFolder) return l;
            return null;
        }

        public static bool IsOurLanguageActive()
        {
            var our = FindOurLanguage();
            return our != null && LanguageDatabase.activeLanguage == our;
        }

        // только из главного потока
        public static bool SwitchToOurLanguage()
        {
            try
            {
                var our = FindOurLanguage();
                if (our == null) { SetStatus("Язык 2ClauZ не найден в списке языков (папка " + OurFolder + ")"); return false; }
                if (LanguageDatabase.activeLanguage == our) return false;
                LanguageDatabase.SelectLanguage(our);
                Prefs.Save();
                SetStatus("Язык переключён: " + our.FriendlyNameNative);
                return true;
            }
            catch (Exception e)
            {
                SetStatus("Не удалось сменить язык: " + e.Message);
                return false;
            }
        }

        public static void SetStatus(string msg)
        {
            statusMsg = msg;
            statusTime = Time.realtimeSinceStartup;
        }
    }

    [HarmonyPatch(typeof(MainMenuDrawer), "MainMenuOnGUI")]
    public static class MainMenuPatch
    {
        private static readonly Vector2 ButtonSize = new Vector2(220f, 38f);
        private static readonly string[] Spinner = { "|", "/", "—", "\\" };

        static void Postfix()
        {
            try
            {
                // по завершении установки — модальное окно с единственной кнопкой ОК
                if (UpdaterState.pendingRestartDialog)
                {
                    UpdaterState.pendingRestartDialog = false;
                    Find.WindowStack.Add(new Dialog_MessageBox(
                        "Перевод «2ClauZ» обновлён до " + UpdaterState.localVersion + ".\n\n" +
                        "Язык игры — «Русский (2ClauZ)», мод перевода модов будет включён.\n" +
                        "После нажатия ОК игра перезапустится.",
                        "OK", OnConfirmRestart));
                }

                if (UpdaterState.wantAutoSwitch)
                {
                    UpdaterState.wantAutoSwitch = false;
                    // автопочинка конфига при старте: ровно один перезапуск. Мод включаем сам, только если игра его видит
                    // (иначе после рестарта он снова "выключен" -> петля перезапусков)
                    bool langOff = !UpdaterState.IsOurLanguageActive() && UpdaterState.FindOurLanguage() != null;
                    bool modOff = !ModsConfig.IsActive(ModsPackId) && ModLister.GetModWithIdentifier(ModsPackId) != null;
                    if (UpdaterState.AutoSwitchEnabled && (langOff || modOff || QuickAreaDup())) { ApplyFinalConfig(); GenCommandLine.Restart(); }
                }

                // автопроверка один раз при первом показе меню (если автопереключение включено)
                if (!UpdaterState.autoChecked)
                {
                    UpdaterState.autoChecked = true;
                    if (UpdaterState.AutoSwitchEnabled) CheckVersion();
                }

                // пока фоновый поток работает — крутим индикатор и показываем, сколько прошло
                bool busy = UpdaterState.workThread != null && UpdaterState.workThread.IsAlive;
                // файлы на месте и версия свежая, но конфиг игры не в финальном состоянии -> кнопка чинит без скачивания
                string cfg = (UpdaterState.checkedOnce && !busy && !UpdaterState.updateAvailable) ? ConfigIssues() : "";
                bool fixFiles = UpdaterState.updateAvailable && !string.IsNullOrEmpty(UpdaterState.missingFiles)
                    && UpdaterState.remoteVersion == UpdaterState.localVersion;
                var rect = new Rect(16f, Screen.height - 120f, cfg != "" ? 360f : ButtonSize.x, ButtonSize.y);
                if (busy && UpdaterState.busySince < 0f) UpdaterState.busySince = Time.realtimeSinceStartup;
                if (!busy) UpdaterState.busySince = -1f;
                float busyFor = busy ? Time.realtimeSinceStartup - UpdaterState.busySince : 0f;
                string spin = Spinner[(int)(busyFor * 8f) % Spinner.Length];
                string label;
                if (UpdaterState.downloading)
                    label = spin + "  Обновление перевода  " + spin;
                else if (busy)
                    label = spin + "  Проверка перевода  " + spin;
                else if (fixFiles)
                    label = "✔ Починить перевод";
                else if (UpdaterState.updateAvailable)
                    label = "✔ Обновить перевод";
                else if (cfg != "")
                    label = "✔ Починить: включить перевод в игре";
                else if (UpdaterState.checkedOnce)
                    label = "Перевод в порядке";
                else
                    label = "Проверить перевод";

                Color prev = GUI.color;
                if (UpdaterState.updateAvailable || cfg != "") GUI.color = new Color(0.4f, 1f, 0.4f);
                if (Widgets.ButtonText(rect, label, true, true, true))
                {
                    if (UpdaterState.updateAvailable) StartDownload();
                    else if (cfg != "")
                        Find.WindowStack.Add(new Dialog_MessageBox(
                            "Перевод скачан, но игра не в финальном состоянии:\n" + cfg + ".\n\n" +
                            "После нажатия ОК это будет исправлено и игра перезапустится.",
                            "OK", OnConfirmRestart, "Отмена", null));
                    else CheckVersion();
                }
                GUI.color = prev;

                // status line
                if (busy)
                {
                    // строка не гаснет, пока идёт работа: текущий шаг + секунды
                    var labelRect = new Rect(rect.x, rect.yMax + 4f, 520f, 24f);
                    GUI.Label(labelRect, spin + " " + (string.IsNullOrEmpty(UpdaterState.statusMsg) ? "Работаю" : UpdaterState.statusMsg.TrimEnd('.'))
                        + " · " + (int)busyFor + " с");
                    // бегущая полоска под кнопкой
                    var bar = new Rect(rect.x, rect.yMax + 1f, rect.width, 3f);
                    Widgets.DrawBoxSolid(bar, new Color(1f, 1f, 1f, 0.12f));
                    float seg = rect.width * 0.25f;
                    float x = ((busyFor * 0.6f) % 1f) * (rect.width + seg) - seg;
                    float x0 = Mathf.Max(x, 0f), x1 = Mathf.Min(x + seg, rect.width);
                    if (x1 > x0) Widgets.DrawBoxSolid(new Rect(rect.x + x0, bar.y, x1 - x0, bar.height), new Color(0.4f, 1f, 0.4f, 0.9f));
                }
                else if (cfg != "" || fixFiles)
                {
                    // постоянная подсказка: что именно кнопка починит
                    var labelRect = new Rect(rect.x, rect.yMax + 4f, 700f, 24f);
                    GUI.Label(labelRect, "Не хватает: " + (fixFiles ? UpdaterState.missingFiles : cfg));
                }
                else if (!string.IsNullOrEmpty(UpdaterState.statusMsg) &&
                    Time.realtimeSinceStartup - UpdaterState.statusTime < 5f)
                {
                    var labelRect = new Rect(rect.x, rect.yMax + 4f, 520f, 24f);
                    GUI.Label(labelRect, UpdaterState.statusMsg);
                }
            }
            catch (Exception e)
            {
                Log.Warning("[2ClauZ Updater] menu patch error: " + e.Message);
            }
        }

        private static void CheckVersion()
        {
            if (UpdaterState.workThread != null && UpdaterState.workThread.IsAlive) return;
            UpdaterState.workThread = new Thread(DoCheckVersion);
            UpdaterState.workThread.IsBackground = true;
            UpdaterState.workThread.Start();
        }

        private static void DoCheckVersion()
        {
            try
            {
                UpdaterState.SetStatus("Проверка версии...");
                string url = GetRepoUrl() + "/version.txt";
                string remote = DownloadText(url).Trim();
                UpdaterState.remoteVersion = remote;

                string local = ReadLocalVersion();
                UpdaterState.localVersion = local;
                UpdaterState.SetStatus("Проверка установленных файлов...");
                UpdaterState.missingFiles = FindMissingFiles();
                UpdaterState.checkedOnce = true;

                if (remote != local || UpdaterState.missingFiles != "")
                {
                    // любая нехватка файлов чинится полной переустановкой
                    UpdaterState.updateAvailable = true;
                    if (remote != local)
                        UpdaterState.SetStatus("Доступна версия: " + remote + " (локальная: " +
                            (string.IsNullOrEmpty(local) ? "нет" : local) + ")");
                    else
                        UpdaterState.SetStatus("Не хватает: " + UpdaterState.missingFiles);
                }
                else
                {
                    UpdaterState.updateAvailable = false;
                    UpdaterState.SetStatus("Перевод актуален (" + remote + ")");
                    UpdaterState.wantAutoSwitch = true;
                }
            }
            catch (Exception e)
            {
                UpdaterState.SetStatus("Ошибка проверки: " + e.Message);
            }
        }

        private const string ModsPackId = "2clauz.rumods";

        private static readonly string[] Packages = { "Core", "Royalty", "Ideology", "Biotech", "Anomaly", "Odyssey" };

        private static string GameRoot()
        {
            // 1.7 (Brutal): Assembly-CSharp.dll лежит прямо в корне игры
            return Path.GetFullPath(Path.GetDirectoryName(Assembly.GetAssembly(typeof(Verse.Root)).Location));
        }

        // установленный мод перевода модов (папка с About.xml и Languages) или null
        private static string ModsPackDir()
        {
            foreach (string name in new[] { "ClauZ_RU_ModsPack", "2ClauZ_RU_Mods" })
            {
                string d = Path.Combine(GameRoot(), "Mods", name);
                if (File.Exists(Path.Combine(d, "About", "About.xml")) && Directory.Exists(Path.Combine(d, "Languages"))) return d;
            }
            return null;
        }

        // финальное состояние на диске (фоновый поток); пусто = всё на месте
        private static string FindMissingFiles()
        {
            var miss = new List<string>();
            try
            {
                string data = Path.Combine(GameRoot(), "Data");
                foreach (string pkg in Packages)
                {
                    if (!Directory.Exists(Path.Combine(data, pkg))) continue; // DLC не куплено
                    string langs = Path.Combine(data, pkg, "Languages");
                    string ours = Path.Combine(langs, UpdaterState.OurFolder);
                    if (!Directory.Exists(ours) || Directory.GetFiles(ours, "*.xml", SearchOption.AllDirectories).Length == 0)
                        miss.Add("язык " + pkg);
                    else if (Directory.Exists(Path.Combine(langs, "Russian")))
                        miss.Add("дубль языка в " + pkg);
                }
                if (!File.Exists(Path.Combine(data, "Core", "Languages", UpdaterState.OurFolder, MergedMark)))
                    miss.Add("сборка поверх официального перевода");
                if (ModsPackDir() == null) miss.Add("мод перевода модов");
            }
            catch (Exception e) { miss.Add("проверка файлов: " + e.Message); }
            return string.Join(", ", miss.ToArray());
        }

        // финальное состояние конфига игры (главный поток); пусто = всё включено
        private static string ConfigIssues()
        {
            var miss = new List<string>();
            if (!ModsConfig.IsActive(ModsPackId)) miss.Add("мод перевода модов выключен");
            if (!UpdaterState.IsOurLanguageActive()) miss.Add("язык не «Русский (2ClauZ)»");
            if (QuickAreaDup()) miss.Add("включены две копии Quick Area");
            return string.Join(", ", miss.ToArray());
        }

        // оригинал Quick Area из Steam и наш форк 1.7 — одна и та же сборка: вместе патчат клик дважды и зона не ставится
        private const string QuickAreaOrig = "hellrevenger.quickarea", QuickAreaFork = "2clauz.quickarea17";
        private static bool QuickAreaDup()
        {
            try { return ModsConfig.IsActive(QuickAreaOrig) && ModsConfig.IsActive(QuickAreaFork); }
            catch { return false; }
        }

        // выставить наш язык и включить мод перевода модов; применяется перезапуском
        private static void ApplyFinalConfig()
        {
            try
            {
                var our = UpdaterState.FindOurLanguage();
                // язык мог появиться только что (свежая установка) — тогда его ещё нет в базе, ставим по имени папки
                Prefs.LangFolderName = our != null ? our.folderName : UpdaterState.OurFolder;
            }
            catch (Exception e) { Log.Warning("[2ClauZ Updater] язык не выставлен: " + e.Message); }
            try { Prefs.Save(); }
            catch { }
            // перевод модов лежит отдельным модом: установлен, но выключен -> строки модов остаются английскими
            try
            {
                if (!ModsConfig.IsActive(ModsPackId))
                {
                    ModsConfig.SetActive(ModsPackId, true);
                    ModsConfig.Save();
                    Log.Message("[2ClauZ Updater] включён мод перевода модов " + ModsPackId);
                }
            }
            catch (Exception e) { Log.Warning("[2ClauZ Updater] мод " + ModsPackId + " не включён: " + e.Message); }
            try
            {
                if (QuickAreaDup())
                {
                    ModsConfig.SetActive(QuickAreaOrig, false);
                    ModsConfig.Save();
                    Log.Message("[2ClauZ Updater] выключен дубль Quick Area " + QuickAreaOrig + ", оставлен " + QuickAreaFork);
                }
            }
            catch (Exception e) { Log.Warning("[2ClauZ Updater] дубль Quick Area не выключен: " + e.Message); }
        }

        private static void OnConfirmRestart()
        {
            // перезапуск штатным GenCommandLine.Restart() (RestartManager.RequestRestart + Root.Shutdown; Waylith сам стартует новый процесс)
            ApplyFinalConfig();
            GenCommandLine.Restart();
        }

        private static void StartDownload()
        {
            if (UpdaterState.workThread != null && UpdaterState.workThread.IsAlive) return;
            UpdaterState.downloading = true;
            UpdaterState.workThread = new Thread(DoDownload);
            UpdaterState.workThread.IsBackground = true;
            UpdaterState.workThread.Start();
        }

        private static void DoDownload()
        {
            try
            {
                UpdaterState.SetStatus("Скачивание перевода...");
                // 1.7 (Brutal): Assembly-CSharp.dll лежит прямо в корне игры
                string gameRoot = Path.GetFullPath(Path.GetDirectoryName(
                    Assembly.GetAssembly(typeof(Verse.Root)).Location));
                string dataDir = Path.Combine(gameRoot, "Data");

                string[] packages = { "Core", "Royalty", "Ideology", "Biotech", "Anomaly", "Odyssey" };

                UpdaterState.SetStatus("Скачивание архива...");
                string tempZip = Path.Combine(Path.GetTempPath(), "rimworld-ru-update.zip");
                DownloadFile("https://github.com/JerenisusJJ/RimWorld-ru/archive/refs/heads/master.zip", tempZip);

                UpdaterState.SetStatus("Распаковка...");
                string tempDir = Path.Combine(Path.GetTempPath(), "rimworld-ru-update");
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
                System.IO.Compression.ZipFile.ExtractToDirectory(tempZip, tempDir);
                File.Delete(tempZip);

                // GitHub archive puts files in <repo>-main/ subdirectory
                string archiveRoot = Path.Combine(tempDir, "RimWorld-ru-master");
                if (!Directory.Exists(archiveRoot))
                {
                    // try to find the subdirectory
                    var dirs = Directory.GetDirectories(tempDir);
                    if (dirs.Length > 0) archiveRoot = dirs[0];
                }

                foreach (string pkg in packages)
                {
                    string srcDir = Path.Combine(archiveRoot, pkg, "Languages", "Russian");
                    string dstDir = Path.Combine(dataDir, pkg, "Languages", UpdaterState.OurFolder);
                    string oldDir = Path.Combine(dataDir, pkg, "Languages", "Russian");
                    if (Directory.Exists(oldDir)) // устаревшая папка со старым именем -> в резерв, не оставлять дубль языка
                    {
                        string bk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                            "RimWorld_lang_backup_2026-10-19", "updater_old_Russian", pkg);
                        try { if (Directory.Exists(bk)) Directory.Delete(bk, true); Directory.CreateDirectory(Path.GetDirectoryName(bk)); Directory.Move(oldDir, bk); }
                        catch (Exception ex) { Log.Warning("[2ClauZ Updater] не удалось убрать " + oldDir + ": " + ex.Message); }
                    }
                    // база — официальный перевод пакета, наши правки поверх по ключам
                    string baseTar = LangMerge.FindOfficialTar(Path.Combine(dataDir, pkg, "Languages"));
                    if (Directory.Exists(srcDir) || baseTar != null)
                    {
                        UpdaterState.SetStatus("Сборка перевода: " + pkg + "...");
                        Log.Message("[2ClauZ Updater] " + pkg + ": " + LangMerge.Build(baseTar, srcDir, dstDir));
                    }
                }

                // mod translations: ModsPack → local mod folder
                string modsPackSrc = Path.Combine(archiveRoot, "ModsPack");
                if (Directory.Exists(modsPackSrc))
                {
                    UpdaterState.SetStatus("Установка перевода модов...");
                    string modsDir = Path.Combine(gameRoot, "Mods");
                    // существующий канонический мод обновляем на месте (без дубля packageId),
                    // новым подписчикам ставим свежую папку
                    string localModDir = Directory.Exists(Path.Combine(modsDir, "ClauZ_RU_ModsPack"))
                        ? Path.Combine(modsDir, "ClauZ_RU_ModsPack")
                        : Path.Combine(modsDir, "2ClauZ_RU_Mods");
                    if (Directory.Exists(localModDir)) Directory.Delete(localModDir, true);
                    CopyDirectory(modsPackSrc, localModDir);
                    UpdaterState.SetStatus("Перевод модов установлен: " + Path.GetFileName(localModDir));
                }

                // write version file
                File.WriteAllText(
                    Path.Combine(dataDir, "Core", "Languages", UpdaterState.OurFolder, "_version.txt"),
                    UpdaterState.remoteVersion);
                // метка: папка собрана поверх официальной базы (старые установки без неё — неполные)
                File.WriteAllText(
                    Path.Combine(dataDir, "Core", "Languages", UpdaterState.OurFolder, MergedMark), "1");

                // cleanup
                Directory.Delete(tempDir, true);

                UpdaterState.missingFiles = FindMissingFiles();
                if (UpdaterState.missingFiles != "") Log.Warning("[2ClauZ Updater] после установки всё ещё не хватает: " + UpdaterState.missingFiles);
                UpdaterState.downloading = false;
                UpdaterState.updateAvailable = false;
                UpdaterState.localVersion = UpdaterState.remoteVersion;
                UpdaterState.pendingRestartDialog = true;
                UpdaterState.SetStatus("Перевод обновлён до " + UpdaterState.remoteVersion + "!");
            }
            catch (Exception e)
            {
                UpdaterState.downloading = false;
                UpdaterState.SetStatus("Ошибка обновления: " + e.Message);
            }
        }

        private static string GetRepoUrl()
        {
            // Can be overridden via environment or settings
            return "https://raw.githubusercontent.com/JerenisusJJ/RimWorld-ru/master";
        }

        private const string MergedMark = "_merged.txt";

        private static string ReadLocalVersion()
        {
            try
            {
                string gameRoot = Path.GetFullPath(Path.GetDirectoryName(
                    Assembly.GetAssembly(typeof(Verse.Root)).Location));
                string vf = Path.Combine(gameRoot, "Data", "Core", "Languages", UpdaterState.OurFolder, "_version.txt");
                // без метки сборки установка неполная (одна дельта без базы) — считаем, что перевода нет
                if (!File.Exists(Path.Combine(Path.GetDirectoryName(vf), MergedMark))) return "";
                if (File.Exists(vf)) return File.ReadAllText(vf).Trim();
            }
            catch { }
            return "";
        }

        private static readonly System.Net.Http.HttpClient _http = new System.Net.Http.HttpClient();

        private static string DownloadText(string url)
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "RimWorld-2ClauZ-Updater");
            return _http.GetStringAsync(url).GetAwaiter().GetResult();
        }

        private static void DownloadFile(string url, string dest)
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "RimWorld-2ClauZ-Updater");
            var bytes = _http.GetByteArrayAsync(url).GetAwaiter().GetResult();
            File.WriteAllBytes(dest, bytes);
        }

        private static void CopyDirectory(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                string rel = file.Substring(src.Length + 1);
                string target = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }
    }
}
