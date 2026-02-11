using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.IO;
using HarmonyLib;
using BepInEx.Logging;
using BepInEx;
using UnityEngine;
using System.Collections;
using UnityEngine.UI;

namespace SelectiveHider.Patches
{
    /// <summary>
    /// Интеграция с модом PeakStats для предотвращения бесконтрольного дублирования баров
    /// </summary>
    public class PeakStatsIntegration
    {
        private static readonly ManualLogSource _logger = BepInEx.Logging.Logger.CreateLogSource("PeakStatsIntegration");

        private Coroutine _resumeCoroutine;
        private static Dictionary<Character, float> _cachedDistances = new Dictionary<Character, float>();
        private static readonly float QUICK_CHECK_INTERVAL = 1.0f; // УВЕЛИЧЕНО с 0.3f
        private static float _lastQuickCheckTime = 0f;
        private static Dictionary<Character, BarState> _barStates = new Dictionary<Character, BarState>();
        private static readonly float HYSTERESIS_THRESHOLD = 2f;
        private static float _hysteresisShowDistance = 8.5f;
        private static float _hysteresisHideDistance = 11.5f;
        private static Type _verticalLayoutGroupType;
        private static Dictionary<Character, Transform> _barTransforms = new Dictionary<Character, Transform>();
        private static List<Character> _activeBarsOrder = new List<Character>();
        private static Dictionary<Character, float> _lastKnownDistances = new Dictionary<Character, float>();
        private static readonly float DISTANCE_UPDATE_INTERVAL = 1.0f; // УВЕЛИЧЕНО с 0.3f
        private static float _lastDistanceUpdateTime = 0f;
        private static Dictionary<Character, bool> _lastActiveState = new Dictionary<Character, bool>();
        private static float _lastLayoutUpdateTime = 0f;
        private static readonly float LAYOUT_UPDATE_INTERVAL = 1.0f; // УВЕЛИЧЕНО с 0.3f
        private static MethodInfo _calculateLayoutInputMethod;
        private static MethodInfo _setLayoutVerticalMethod;
        private static FieldInfo _verticalLayoutGroupField;
        private Coroutine _updateCoroutine;
        private static bool _isPeakStatsPaused = false;

        // Кэшированные типы и поля PeakStats
        private static Type _proximityStaminaManagerType;
        private static Type _characterStaminaBarType;
        private static Type _entryType;
        private static object _proximityStaminaManagerInstance;

        // Ссылки на оригинальные поля через рефлексию
        private static FieldInfo _staminaBarsField;
        private static FieldInfo _disabledStaminaBarsField;
        private static PropertyInfo _observedCharacterProperty;
        private static MethodInfo _animateDisableMethod;
        private static MethodInfo _animateEnableMethod;
        private static FieldInfo _isEnabledField;
        private static FieldInfo _animateEnableDisableCoroutineField;
        private static Dictionary<Character, int> _barSiblingIndexCache = new Dictionary<Character, int>();
        private static List<Character> _sortedActiveBars = new List<Character>();

        // Порог дистанции
        private static float _proximityThreshold = 20f;
        private static float _adjustedProximityThreshold = 20f;

        // Для отслеживания последнего состояния
        private static bool _wasInCleanMode = false;
        private static float _resumeTime = 0f;
        private static readonly float POST_RESUME_DISABLE_TIME = 1.5f;

        // Кэш для баров и персонажей
        private static Dictionary<Character, object> _cachedStaminaBars = new Dictionary<Character, object>();
        private static Dictionary<Character, float> _lastDistanceChecks = new Dictionary<Character, float>();
        private static float _lastCacheUpdateTime = 0f;
        private static readonly float CACHE_UPDATE_INTERVAL = 3f; // УВЕЛИЧЕНО с 2f
        private static readonly float DISTANCE_CHECK_INTERVAL = 1.0f; // УВЕЛИЧЕНО с 0.5f

        // Для синхронизации анимаций
        private static bool _isProcessingResume = false;
        private static float _lastBarActionTime = 0f;
        private static readonly float MIN_BAR_ACTION_INTERVAL = 0.5f; // УВЕЛИЧЕНО с 0.3f

        // Оптимизация: флаги для пропуска ненужных проверок
        private static bool _hasOtherPlayers = false;
        private static float _lastPlayerCheckTime = 0f;
        private static readonly float PLAYER_CHECK_INTERVAL = 2.0f;
        private static int _lastPlayerCount = 0;

        // Оптимизация: кэшированные компоненты
        private static VerticalLayoutGroup _cachedVerticalLayoutGroup = null;
        private static ContentSizeFitter _cachedContentSizeFitter = null;
        private static MonoBehaviour _cachedMonoBehaviour = null;

        public bool IsInitialized { get; private set; }

        private enum BarState
        {
            Unknown,
            Showing,
            Hiding,
            Visible,
            Hidden
        }

        /// <summary>
        /// Инициализация интеграции с PeakStats
        /// </summary>
        public void Initialize()
        {
            try
            {
                _logger.LogInfo("Начинаем инициализацию интеграции с PeakStats...");

                var peakStatsAssembly = FindPeakStatsAssemblyInLoadedAssemblies();

                if (peakStatsAssembly == null)
                {
                    peakStatsAssembly = LoadPeakStatsAssemblyFromFile();
                }

                if (peakStatsAssembly == null)
                {
                    _logger.LogWarning("PeakStats не найден. Убедитесь, что мод PeakStats установлен и активен.");
                    return;
                }

                _logger.LogInfo($"Найдена сборка PeakStats: {peakStatsAssembly.FullName}");

                // Получаем типы
                _proximityStaminaManagerType = peakStatsAssembly.GetType("PeakStats.MonoBehaviours.ProximityStaminaManager");
                _characterStaminaBarType = peakStatsAssembly.GetType("PeakStats.MonoBehaviours.CharacterStaminaBar");
                _entryType = peakStatsAssembly.GetType("PeakStats.Entry");

                if (_proximityStaminaManagerType == null || _characterStaminaBarType == null || _entryType == null)
                {
                    _logger.LogWarning("Не удалось найти необходимые типы PeakStats");
                    return;
                }

                _logger.LogInfo("Типы PeakStats успешно загружены");

                _verticalLayoutGroupType = typeof(VerticalLayoutGroup);
                if (_verticalLayoutGroupType != null)
                {
                    _calculateLayoutInputMethod = _verticalLayoutGroupType.GetMethod("CalculateLayoutInputVertical",
                        BindingFlags.Public | BindingFlags.Instance);
                    _setLayoutVerticalMethod = _verticalLayoutGroupType.GetMethod("SetLayoutVertical",
                        BindingFlags.Public | BindingFlags.Instance);
                }

                // Получаем доступ к полям и методам через рефлексию (ОДИН РАЗ при инициализации)
                _staminaBarsField = _proximityStaminaManagerType.GetField("staminaBars",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                _disabledStaminaBarsField = _proximityStaminaManagerType.GetField("disabledStaminaBars",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                _observedCharacterProperty = _characterStaminaBarType.GetProperty("observedCharacter",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                _animateDisableMethod = _characterStaminaBarType.GetMethod("AnimateDisable",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                _animateEnableMethod = _characterStaminaBarType.GetMethod("AnimateEnable",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                _isEnabledField = _characterStaminaBarType.GetField("isEnabled",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                _animateEnableDisableCoroutineField = _characterStaminaBarType.GetField("animateEnableDisableCoroutine",
                    BindingFlags.NonPublic | BindingFlags.Instance);

                if (_staminaBarsField == null || _disabledStaminaBarsField == null ||
                    _observedCharacterProperty == null || _animateDisableMethod == null || _animateEnableMethod == null)
                {
                    _logger.LogWarning("Не удалось получить доступ ко всем необходимым членам PeakStats");
                    return;
                }

                // Пытаемся получить порог дистанции из конфига PeakStats
                TryGetPeakStatsDistanceThreshold(peakStatsAssembly);

                _hysteresisShowDistance = _proximityThreshold - HYSTERESIS_THRESHOLD;
                _hysteresisHideDistance = _proximityThreshold + HYSTERESIS_THRESHOLD;
                _logger.LogInfo($"Гистерезис: показываем при {_hysteresisShowDistance:F1}м, скрываем при {_hysteresisHideDistance:F1}м");

                _adjustedProximityThreshold = _proximityThreshold + 5f;

                // Создаем Harmony патчи
                CreateHarmonyPatches();

                IsInitialized = true;
                _logger.LogInfo($"PeakStats интеграция успешно инициализирована. Порог дистанции: {_proximityThreshold}, Скорректированный: {_adjustedProximityThreshold}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка инициализации PeakStats интеграции: {ex}");
                IsInitialized = false;
            }
        }

        /// <summary>
        /// Проверяет, есть ли другие игроки кроме локального
        /// </summary>
        private bool CheckForOtherPlayers()
        {
            try
            {
                if (Time.time - _lastPlayerCheckTime < PLAYER_CHECK_INTERVAL)
                    return _hasOtherPlayers;

                _lastPlayerCheckTime = Time.time;

                if (_proximityStaminaManagerInstance == null)
                {
                    FindAndCacheProximityStaminaManager();
                    if (_proximityStaminaManagerInstance == null)
                    {
                        _hasOtherPlayers = false;
                        return false;
                    }
                }

                var staminaBars = _staminaBarsField?.GetValue(_proximityStaminaManagerInstance) as IDictionary;
                if (staminaBars == null)
                {
                    _hasOtherPlayers = false;
                    return false;
                }

                int otherPlayerCount = 0;
                foreach (DictionaryEntry entry in staminaBars)
                {
                    var character = entry.Key as Character;
                    if (character != null && !character.IsLocal && IsCharacterValid(character))
                    {
                        otherPlayerCount++;
                        if (otherPlayerCount > 0)
                        {
                            _hasOtherPlayers = true;
                            return true;
                        }
                    }
                }

                _hasOtherPlayers = otherPlayerCount > 0;
                return _hasOtherPlayers;
            }
            catch
            {
                return false;
            }
        }

        private void ManageBarLayout(bool forceImmediate = false)
        {
            try
            {
                // ОПТИМИЗАЦИЯ: Если нет других игроков, пропускаем
                if (!forceImmediate && !CheckForOtherPlayers())
                {
                    _lastLayoutUpdateTime = Time.time;
                    return;
                }

                if (!forceImmediate && Time.time - _lastLayoutUpdateTime < LAYOUT_UPDATE_INTERVAL)
                    return;

                _lastLayoutUpdateTime = Time.time;

                if (_proximityStaminaManagerInstance == null || Character.localCharacter == null)
                    return;

                var staminaBars = _staminaBarsField?.GetValue(_proximityStaminaManagerInstance) as IDictionary;
                if (staminaBars == null)
                    return;

                // ОПТИМИЗАЦИЯ: Быстрая проверка - если нет активных баров, выходим раньше
                bool hasActiveBars = false;
                foreach (DictionaryEntry entry in staminaBars)
                {
                    var character = entry.Key as Character;
                    if (character != null && !character.IsLocal && _lastActiveState.TryGetValue(character, out bool isActive) && isActive)
                    {
                        hasActiveBars = true;
                        break;
                    }
                }

                if (!hasActiveBars && !forceImmediate)
                    return;

                // Собираем информацию об активных барах
                var activeBarInfos = new List<BarLayoutInfo>();
                var inactiveBars = new List<Character>();
                bool layoutNeedsUpdate = false;

                foreach (DictionaryEntry entry in staminaBars)
                {
                    try
                    {
                        var character = entry.Key as Character;
                        var staminaBar = entry.Value;

                        if (character == null || staminaBar == null || character.IsLocal)
                            continue;

                        bool isCharacterValid = IsCharacterValid(character);

                        float distance = _lastKnownDistances.ContainsKey(character)
                            ? _lastKnownDistances[character]
                            : GetDistanceToCharacterSafe(character);

                        bool shouldBeActive = isCharacterValid && distance <= _proximityThreshold;

                        var barGameObject = GetGameObjectFromBar(staminaBar);
                        if (barGameObject == null)
                            continue;

                        bool isCurrentlyActive = barGameObject.activeSelf;

                        bool isCloseButInactive = shouldBeActive && !isCurrentlyActive;

                        bool stateChanged = false;
                        if (_lastActiveState.TryGetValue(character, out bool lastActive))
                        {
                            if (lastActive != shouldBeActive)
                            {
                                stateChanged = true;
                                layoutNeedsUpdate = true;
                            }
                        }
                        else
                        {
                            stateChanged = true;
                            layoutNeedsUpdate = true;
                        }

                        _lastActiveState[character] = shouldBeActive;

                        if (stateChanged || isCloseButInactive)
                        {
                            layoutNeedsUpdate = true;
                        }

                        if (shouldBeActive)
                        {
                            activeBarInfos.Add(new BarLayoutInfo
                            {
                                Character = character,
                                StaminaBar = staminaBar,
                                GameObject = barGameObject,
                                Distance = distance,
                                ShouldBeActive = shouldBeActive,
                                IsCurrentlyActive = isCurrentlyActive,
                                StateChanged = stateChanged,
                                IsCloseButInactive = isCloseButInactive
                            });
                        }
                        else
                        {
                            inactiveBars.Add(character);
                            if (isCurrentlyActive)
                            {
                                barGameObject.SetActive(false);
                                try
                                {
                                    if (_isEnabledField != null)
                                        _isEnabledField.SetValue(staminaBar, false);
                                }
                                catch { }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug($"Ошибка в обработке бара: {ex.Message}");
                        continue;
                    }
                }

                bool orderChanged = CheckIfActiveBarOrderChanged(activeBarInfos);

                if (layoutNeedsUpdate || orderChanged || forceImmediate)
                {
                    activeBarInfos.Sort((a, b) => b.Distance.CompareTo(a.Distance));

                    _sortedActiveBars.Clear();
                    _sortedActiveBars.AddRange(activeBarInfos.Select(b => b.Character));

                    for (int i = 0; i < activeBarInfos.Count; i++)
                    {
                        try
                        {
                            var barInfo = activeBarInfos[i];
                            if (barInfo.GameObject == null)
                                continue;

                            barInfo.GameObject.transform.SetSiblingIndex(i);
                            _barSiblingIndexCache[barInfo.Character] = i;

                            if ((!barInfo.IsCurrentlyActive && barInfo.ShouldBeActive) || barInfo.IsCloseButInactive)
                            {
                                SafeInvokeAnimateEnable(barInfo.StaminaBar, barInfo.Character);
                            }
                        }
                        catch { }
                    }

                    int startIndex = activeBarInfos.Count;
                    foreach (var character in inactiveBars)
                    {
                        try
                        {
                            if (staminaBars.Contains(character))
                            {
                                var staminaBar = staminaBars[character];
                                var barGameObject = GetGameObjectFromBar(staminaBar);
                                if (barGameObject != null)
                                {
                                    barGameObject.transform.SetSiblingIndex(startIndex++);
                                }
                            }
                        }
                        catch { }
                    }

                    ForceUpdateBarLayout();
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"Ошибка управления компоновкой: {ex.Message}");
            }
        }

        /// <summary>
        /// Проверяет, является ли персонаж валидным для отображения
        /// </summary>
        private bool IsCharacterValid(Character character)
        {
            if (character == null || character.gameObject == null)
                return false;

            try
            {
                if (!character.gameObject.activeInHierarchy)
                    return false;

                if (character.data == null || character.data.dead)
                    return false;

                if (character.refs == null)
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Безопасно получает расстояние до персонажа
        /// </summary>
        private float GetDistanceToCharacterSafe(Character character)
        {
            try
            {
                if (!IsCharacterValid(character) || Character.localCharacter == null)
                    return float.MaxValue;

                if (!IsCharacterValid(Character.localCharacter))
                    return float.MaxValue;

                if (character.Center == null || Character.localCharacter.Center == null)
                    return float.MaxValue;

                return Vector3.Distance(Character.localCharacter.Center, character.Center);
            }
            catch
            {
                return float.MaxValue;
            }
        }

        /// <summary>
        /// Проверяет, изменился ли порядок активных баров
        /// </summary>
        private bool CheckIfActiveBarOrderChanged(List<BarLayoutInfo> currentActiveBars)
        {
            try
            {
                if (currentActiveBars.Count != _sortedActiveBars.Count)
                    return true;

                _sortedActiveBars.RemoveAll(c => c == null);

                var currentSorted = currentActiveBars
                    .Where(b => b.Character != null)
                    .Select(b => b.Character)
                    .OrderBy(c => c.characterName)
                    .ToList();

                var cachedSorted = _sortedActiveBars
                    .Where(c => c != null)
                    .OrderBy(c => c.characterName)
                    .ToList();

                if (!currentSorted.SequenceEqual(cachedSorted))
                    return true;

                for (int i = 0; i < currentActiveBars.Count - 1; i++)
                {
                    for (int j = i + 1; j < currentActiveBars.Count; j++)
                    {
                        var char1 = currentActiveBars[i].Character;
                        var char2 = currentActiveBars[j].Character;

                        if (char1 == null || char2 == null)
                            continue;

                        int index1 = _sortedActiveBars.IndexOf(char1);
                        int index2 = _sortedActiveBars.IndexOf(char2);

                        if (index1 >= 0 && index2 >= 0)
                        {
                            bool wasChar1BeforeChar2 = index1 < index2;
                            bool shouldChar1BeBeforeChar2 =
                                currentActiveBars[i].Distance > currentActiveBars[j].Distance;

                            if (wasChar1BeforeChar2 != shouldChar1BeBeforeChar2)
                            {
                                return true;
                            }
                        }
                    }
                }

                return false;
            }
            catch
            {
                return true;
            }
        }

        /// <summary>
        /// Информация о баре для компоновки
        /// </summary>
        private class BarLayoutInfo
        {
            public Character Character { get; set; }
            public object StaminaBar { get; set; }
            public GameObject GameObject { get; set; }
            public float Distance { get; set; }
            public bool ShouldBeActive { get; set; }
            public bool IsCurrentlyActive { get; set; }
            public bool StateChanged { get; set; }
            public bool IsCloseButInactive { get; set; }
        }

        /// <summary>
        /// Принудительно обновляет компоновку баров (VerticalLayoutGroup)
        /// </summary>
        private void ForceUpdateBarLayout()
        {
            try
            {
                if (_proximityStaminaManagerInstance == null)
                    return;

                // ОПТИМИЗАЦИЯ: Используем кэшированные компоненты
                if (_cachedVerticalLayoutGroup == null || _cachedContentSizeFitter == null)
                {
                    var staminaBars = _staminaBarsField?.GetValue(_proximityStaminaManagerInstance) as IDictionary;
                    if (staminaBars == null || staminaBars.Count == 0)
                        return;

                    foreach (DictionaryEntry entry in staminaBars)
                    {
                        var staminaBar = entry.Value;
                        if (staminaBar == null)
                            continue;

                        var barGameObject = GetGameObjectFromBar(staminaBar);
                        if (barGameObject == null || barGameObject.transform.parent == null)
                            continue;

                        var parent = barGameObject.transform.parent;
                        _cachedVerticalLayoutGroup = parent.GetComponent<VerticalLayoutGroup>();
                        _cachedContentSizeFitter = parent.GetComponent<ContentSizeFitter>();

                        if (_cachedVerticalLayoutGroup != null)
                            break;
                    }
                }

                if (_cachedVerticalLayoutGroup != null && _cachedVerticalLayoutGroup.isActiveAndEnabled)
                {
                    _cachedVerticalLayoutGroup.CalculateLayoutInputVertical();
                    _cachedVerticalLayoutGroup.SetLayoutVertical();

                    if (_cachedContentSizeFitter != null && _cachedContentSizeFitter.isActiveAndEnabled)
                    {
                        _cachedContentSizeFitter.SetLayoutVertical();
                    }
                }
            }
            catch
            {
                // Сбрасываем кэш при ошибке
                _cachedVerticalLayoutGroup = null;
                _cachedContentSizeFitter = null;
            }
        }

        /// <summary>
        /// Ищет сборку PeakStats в уже загруженных сборках
        /// </summary>
        private Assembly FindPeakStatsAssemblyInLoadedAssemblies()
        {
            try
            {
                var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();

                foreach (var assembly in allAssemblies)
                {
                    try
                    {
                        var assemblyName = assembly.GetName().Name;

                        if (assemblyName == "PeakStats" ||
                            assemblyName == "nickklmao.peakstats" ||
                            assemblyName == "nickklmao-PeakStats" ||
                            (assemblyName.Contains("PeakStats") && !assemblyName.Contains("PEAK")))
                        {
                            var testType = assembly.GetType("PeakStats.MonoBehaviours.ProximityStaminaManager");
                            if (testType != null)
                            {
                                return assembly;
                            }
                        }
                    }
                    catch { }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Загружает сборку PeakStats из файла
        /// </summary>
        private Assembly LoadPeakStatsAssemblyFromFile()
        {
            try
            {
                string pluginsPath = Path.Combine(Paths.BepInExRootPath, "plugins");

                if (!Directory.Exists(pluginsPath))
                {
                    return null;
                }

                string[] possiblePaths = new string[]
                {
                    Path.Combine(pluginsPath, "PeakStats.dll"),
                    Path.Combine(pluginsPath, "nickklmao-PeakStats", "PeakStats.dll"),
                    Path.Combine(pluginsPath, "nickklmao.peakstats", "PeakStats.dll"),
                    Path.Combine(pluginsPath, "PeakStats", "PeakStats.dll")
                };

                foreach (var path in possiblePaths)
                {
                    if (File.Exists(path))
                    {
                        try
                        {
                            var assembly = Assembly.LoadFrom(path);
                            var testType = assembly.GetType("PeakStats.MonoBehaviours.ProximityStaminaManager");
                            if (testType != null)
                            {
                                return assembly;
                            }
                        }
                        catch { }
                    }
                }

                var allDlls = Directory.GetFiles(pluginsPath, "PeakStats.dll", SearchOption.AllDirectories);

                foreach (var dllPath in allDlls)
                {
                    try
                    {
                        var assembly = Assembly.LoadFrom(dllPath);
                        var testType = assembly.GetType("PeakStats.MonoBehaviours.ProximityStaminaManager");
                        if (testType != null)
                        {
                            return assembly;
                        }
                    }
                    catch { }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Пытается получить порог дистанции из конфига PeakStats
        /// </summary>
        private void TryGetPeakStatsDistanceThreshold(Assembly peakStatsAssembly)
        {
            try
            {
                var teammateStaminaBarProximityField = _entryType.GetField("teammateStaminaBarProximity",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

                if (teammateStaminaBarProximityField != null)
                {
                    var configEntry = teammateStaminaBarProximityField.GetValue(null);
                    if (configEntry != null)
                    {
                        var valueProperty = configEntry.GetType().GetProperty("Value");
                        if (valueProperty != null)
                        {
                            _proximityThreshold = (float)valueProperty.GetValue(configEntry);
                            _adjustedProximityThreshold = _proximityThreshold + 5f;
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Создает Harmony патчи для модификации поведения PeakStats
        /// </summary>
        private static void CreateHarmonyPatches()
        {
            try
            {
                var harmony = new Harmony("com.yourname.SelectiveHider.PeakStatsIntegration");

                var updateMethod = _proximityStaminaManagerType.GetMethod("Update",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                if (updateMethod != null)
                {
                    var prefix = new HarmonyMethod(typeof(PeakStatsIntegration)
                        .GetMethod("ProximityStaminaManager_Update_Prefix", BindingFlags.Static | BindingFlags.NonPublic));

                    harmony.Patch(updateMethod, prefix: prefix);
                }

                var characterBarUpdateMethod = _characterStaminaBarType.GetMethod("Update",
                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                if (characterBarUpdateMethod != null)
                {
                    var prefix = new HarmonyMethod(typeof(PeakStatsIntegration)
                        .GetMethod("CharacterStaminaBar_Update_Prefix", BindingFlags.Static | BindingFlags.NonPublic));

                    harmony.Patch(characterBarUpdateMethod, prefix: prefix);
                }
            }
            catch { }
        }

        /// <summary>
        /// Патч для ProximityStaminaManager.Update - останавливаем обновление при паузе
        /// </summary>
        private static bool ProximityStaminaManager_Update_Prefix(object __instance)
        {
            if (!_isPeakStatsPaused)
                return true;

            if (_proximityStaminaManagerInstance == null && __instance != null)
            {
                _proximityStaminaManagerInstance = __instance;
            }

            return false;
        }

        /// <summary>
        /// Патч для CharacterStaminaBar.Update - останавливаем анимации при паузе
        /// </summary>
        private static bool CharacterStaminaBar_Update_Prefix(object __instance)
        {
            return !_isPeakStatsPaused;
        }

        /// <summary>
        /// Приостановить работу PeakStats
        /// </summary>
        public void PausePeakStats()
        {
            if (!IsInitialized)
                return;

            _isPeakStatsPaused = true;
            _wasInCleanMode = true;
            _barStates.Clear();

            StopUpdateCoroutine();

            _lastActiveState.Clear();
            _lastKnownDistances.Clear();
            _cachedDistances.Clear();

            StopAllActiveAnimations();

            FindAndCacheProximityStaminaManager();
        }

        /// <summary>
        /// Останавливает все активные анимации
        /// </summary>
        private void StopAllActiveAnimations()
        {
            try
            {
                if (_proximityStaminaManagerInstance == null)
                    return;

                var staminaBars = _staminaBarsField?.GetValue(_proximityStaminaManagerInstance) as IDictionary;
                if (staminaBars == null)
                    return;

                int stoppedCount = 0;
                foreach (DictionaryEntry entry in staminaBars)
                {
                    var staminaBar = entry.Value;
                    if (staminaBar == null)
                        continue;

                    if (_animateEnableDisableCoroutineField != null)
                    {
                        var coroutine = _animateEnableDisableCoroutineField.GetValue(staminaBar) as Coroutine;
                        if (coroutine != null)
                        {
                            MonoBehaviour monoBehaviour = GetCachedMonoBehaviour();
                            if (monoBehaviour != null)
                            {
                                monoBehaviour.StopCoroutine(coroutine);
                                _animateEnableDisableCoroutineField.SetValue(staminaBar, null);
                                stoppedCount++;
                            }
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Очистка кэшей
        /// </summary>
        private void ClearCaches()
        {
            _cachedStaminaBars.Clear();
            _lastDistanceChecks.Clear();
        }

        /// <summary>
        /// Останавливает корутину обновления
        /// </summary>
        private void StopUpdateCoroutine()
        {
            try
            {
                if (_updateCoroutine != null)
                {
                    MonoBehaviour monoBehaviour = GetCachedMonoBehaviour();
                    if (monoBehaviour != null)
                    {
                        monoBehaviour.StopCoroutine(_updateCoroutine);
                    }
                    _updateCoroutine = null;
                }
            }
            catch { }
        }

        /// <summary>
        /// Находит и кэширует ProximityStaminaManager
        /// </summary>
        private void FindAndCacheProximityStaminaManager()
        {
            try
            {
                if (_proximityStaminaManagerInstance == null)
                {
                    var allManagers = UnityEngine.Object.FindObjectsOfType(_proximityStaminaManagerType);
                    if (allManagers.Length > 0)
                    {
                        _proximityStaminaManagerInstance = allManagers[0];
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Безопасно запускает корутину
        /// </summary>
        private Coroutine StartCoroutine(IEnumerator routine)
        {
            try
            {
                MonoBehaviour monoBehaviour = GetCachedMonoBehaviour();
                if (monoBehaviour != null)
                {
                    return monoBehaviour.StartCoroutine(routine);
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Возобновить работу PeakStats
        /// </summary>
        public void ResumePeakStats()
        {
            if (!IsInitialized)
                return;

            _isPeakStatsPaused = false;
            _resumeTime = Time.time;
            _isProcessingResume = true;

            ResetDisabledStaminaBarsFlag();

            _resumeCoroutine = StartCoroutine(ResumePeakStatsDelayed());
        }

        /// <summary>
        /// Цикл обновления с оптимальным интервалом
        /// </summary>
        private IEnumerator UpdateLoop()
        {
            while (!_isPeakStatsPaused)
            {
                yield return new WaitForSeconds(1.0f); // УВЕЛИЧЕНО с 0.5f
                UpdatePeakStatsIntegration();
            }
        }

        /// <summary>
        /// Сбрасывает флаг disabledStaminaBars в PeakStats
        /// </summary>
        private void ResetDisabledStaminaBarsFlag()
        {
            try
            {
                if (_proximityStaminaManagerInstance != null && _disabledStaminaBarsField != null)
                {
                    _disabledStaminaBarsField.SetValue(_proximityStaminaManagerInstance, false);
                }
            }
            catch { }
        }

        /// <summary>
        /// Возобновление работы PeakStats с небольшой задержкой
        /// </summary>
        private IEnumerator ResumePeakStatsDelayed()
        {
            yield return null;

            FindAndCacheProximityStaminaManager();

            yield return new WaitForSeconds(0.2f);

            ProcessBarsOnResume();

            _wasInCleanMode = false;
            _isProcessingResume = false;

            _updateCoroutine = StartCoroutine(UpdateLoop());
        }

        /// <summary>
        /// Обрабатывает бары при возобновлении работы PeakStats
        /// </summary>
        private void ProcessBarsOnResume()
        {
            try
            {
                if (_proximityStaminaManagerInstance == null || Character.localCharacter == null)
                {
                    return;
                }

                var staminaBars = _staminaBarsField?.GetValue(_proximityStaminaManagerInstance) as IDictionary;
                if (staminaBars == null)
                {
                    return;
                }

                _cachedDistances.Clear();

                ManageBarLayout(true);
            }
            catch
            {
                _wasInCleanMode = false;
            }
        }

        /// <summary>
        /// Безопасный вызов анимации деактивации
        /// </summary>
        private void SafeInvokeAnimateDisable(object staminaBar, Character character)
        {
            try
            {
                if (_animateDisableMethod != null && staminaBar != null)
                {
                    if (_animateEnableDisableCoroutineField != null)
                    {
                        var coroutine = _animateEnableDisableCoroutineField.GetValue(staminaBar) as Coroutine;
                        if (coroutine != null)
                        {
                            MonoBehaviour monoBehaviour = GetCachedMonoBehaviour();
                            if (monoBehaviour != null)
                            {
                                monoBehaviour.StopCoroutine(coroutine);
                            }
                            _animateEnableDisableCoroutineField.SetValue(staminaBar, null);
                        }
                    }

                    if (_isEnabledField != null)
                    {
                        _isEnabledField.SetValue(staminaBar, false);
                    }

                    _animateDisableMethod.Invoke(staminaBar, null);
                    _barStates[character] = BarState.Hiding;

                    StartCoroutine(UpdateBarStateAfterDelay(character, BarState.Hidden, 0.5f));
                }
            }
            catch { }
        }

        /// <summary>
        /// Безопасный вызов анимации активации
        /// </summary>
        private void SafeInvokeAnimateEnable(object staminaBar, Character character)
        {
            try
            {
                if (_animateEnableMethod != null && staminaBar != null)
                {
                    if (_animateEnableDisableCoroutineField != null)
                    {
                        var coroutine = _animateEnableDisableCoroutineField.GetValue(staminaBar) as Coroutine;
                        if (coroutine != null)
                        {
                            MonoBehaviour monoBehaviour = GetCachedMonoBehaviour();
                            if (monoBehaviour != null)
                            {
                                monoBehaviour.StopCoroutine(coroutine);
                            }
                            _animateEnableDisableCoroutineField.SetValue(staminaBar, null);
                        }
                    }

                    if (_isEnabledField != null)
                    {
                        _isEnabledField.SetValue(staminaBar, true);
                    }

                    _animateEnableMethod.Invoke(staminaBar, null);
                    _barStates[character] = BarState.Showing;

                    StartCoroutine(UpdateBarStateAfterDelay(character, BarState.Visible, 0.5f));
                }
            }
            catch { }
        }

        private IEnumerator UpdateBarStateAfterDelay(Character character, BarState newState, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (_barStates.ContainsKey(character))
            {
                _barStates[character] = newState;
            }
        }

        private void ForceSyncBarStates()
        {
            try
            {
                if (_proximityStaminaManagerInstance == null || Character.localCharacter == null)
                    return;

                var staminaBars = _staminaBarsField?.GetValue(_proximityStaminaManagerInstance) as IDictionary;
                if (staminaBars == null)
                    return;

                foreach (DictionaryEntry entry in staminaBars)
                {
                    var character = entry.Key as Character;
                    var staminaBar = entry.Value;

                    if (character == null || staminaBar == null || character.IsLocal)
                        continue;

                    float currentDistance = _cachedDistances.ContainsKey(character)
                        ? _cachedDistances[character]
                        : Vector3.Distance(Character.localCharacter.Center, character.Center);

                    var barGameObject = GetGameObjectFromBar(staminaBar);
                    if (barGameObject == null)
                        continue;

                    bool isCurrentlyActive = barGameObject.activeSelf;

                    bool isEnabled = false;
                    if (_isEnabledField != null)
                    {
                        try
                        {
                            isEnabled = (bool)_isEnabledField.GetValue(staminaBar);
                        }
                        catch { }
                    }

                    bool shouldBeActive = currentDistance <= _hysteresisShowDistance;
                    bool shouldBeHidden = currentDistance >= _hysteresisHideDistance;

                    if (!shouldBeActive && !shouldBeHidden)
                    {
                        shouldBeActive = isCurrentlyActive && isEnabled;
                    }

                    if (shouldBeActive && (!isCurrentlyActive || !isEnabled))
                    {
                        SafeInvokeAnimateEnable(staminaBar, character);
                    }
                    else if (!shouldBeActive && (isCurrentlyActive || isEnabled))
                    {
                        SafeInvokeAnimateDisable(staminaBar, character);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Получает GameObject из бара или компонента
        /// </summary>
        private GameObject GetGameObjectFromBar(object obj)
        {
            try
            {
                if (obj == null)
                    return null;

                var componentProperty = obj.GetType().GetProperty("gameObject");
                if (componentProperty != null)
                {
                    return componentProperty.GetValue(obj) as GameObject;
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Периодическое обновление интеграции PeakStats
        /// </summary>
        public void UpdatePeakStatsIntegration()
        {
            try
            {
                if (!IsInitialized)
                    return;

                if (_isPeakStatsPaused)
                    return;

                // ОПТИМИЗАЦИЯ: Если нет других игроков, пропускаем все проверки
                if (!CheckForOtherPlayers())
                {
                    _lastQuickCheckTime = Time.time;
                    return;
                }

                if (Time.time - _resumeTime < POST_RESUME_DISABLE_TIME)
                    return;

                if (_isProcessingResume)
                    return;

                if (Time.time - _lastQuickCheckTime >= QUICK_CHECK_INTERVAL)
                {
                    QuickDistanceCheck();
                    _lastQuickCheckTime = Time.time;
                }

                if (Time.time % 2f < 0.1f) // УВЕЛИЧЕНО с 1f до 2f
                {
                    CheckAndFixBarStates();
                }
            }
            catch { }
        }

        /// <summary>
        /// Быстрая проверка расстояний и принудительная активация/деактивация баров
        /// </summary>
        private void QuickDistanceCheck()
        {
            try
            {
                if (_proximityStaminaManagerInstance == null || Character.localCharacter == null)
                    return;

                var staminaBars = _staminaBarsField?.GetValue(_proximityStaminaManagerInstance) as IDictionary;
                if (staminaBars == null)
                    return;

                foreach (DictionaryEntry entry in staminaBars)
                {
                    var character = entry.Key as Character;
                    var staminaBar = entry.Value;

                    if (character == null || staminaBar == null || character.IsLocal)
                        continue;

                    float currentDistance = Vector3.Distance(Character.localCharacter.Center, character.Center);
                    _cachedDistances[character] = currentDistance;

                    var barGameObject = GetGameObjectFromBar(staminaBar);
                    if (barGameObject == null)
                        continue;

                    bool isCurrentlyActive = barGameObject.activeSelf;

                    bool isEnabled = false;
                    if (_isEnabledField != null)
                    {
                        try
                        {
                            isEnabled = (bool)_isEnabledField.GetValue(staminaBar);
                        }
                        catch { }
                    }

                    if (!_barStates.TryGetValue(character, out var barState))
                    {
                        barState = isCurrentlyActive ? BarState.Visible : BarState.Hidden;
                        _barStates[character] = barState;
                    }

                    bool shouldShow = currentDistance <= _hysteresisShowDistance;
                    bool shouldHide = currentDistance >= _hysteresisHideDistance;

                    if (!shouldShow && !shouldHide)
                    {
                        shouldShow = barState == BarState.Visible || barState == BarState.Showing;
                        shouldHide = barState == BarState.Hidden || barState == BarState.Hiding;
                    }

                    bool needsToShow = shouldShow && !isCurrentlyActive && !isEnabled;
                    bool needsToHide = shouldHide && isCurrentlyActive && isEnabled;

                    if (needsToShow)
                    {
                        SafeInvokeAnimateEnable(staminaBar, character);
                        _barStates[character] = BarState.Showing;
                    }
                    else if (needsToHide)
                    {
                        SafeInvokeAnimateDisable(staminaBar, character);
                        _barStates[character] = BarState.Hiding;
                    }
                    else if (isCurrentlyActive && shouldShow)
                    {
                        _barStates[character] = BarState.Visible;
                    }
                    else if (!isCurrentlyActive && shouldHide)
                    {
                        _barStates[character] = BarState.Hidden;
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Проверяет и исправляет состояния баров
        /// </summary>
        private void CheckAndFixBarStates()
        {
            try
            {
                if (_proximityStaminaManagerInstance == null || Character.localCharacter == null)
                    return;

                if (Time.time % 5f < 0.1f) // УВЕЛИЧЕНО с 3f до 5f
                {
                    ForceSyncBarStates();
                }

                ManageBarLayout();
            }
            catch { }
        }

        /// <summary>
        /// Сбрасывает состояние интеграции (вызывается при загрузке новой сцены)
        /// </summary>
        public void ResetOnSceneLoad()
        {
            try
            {
                StopUpdateCoroutine();
                if (_resumeCoroutine != null)
                {
                    MonoBehaviour monoBehaviour = GetCachedMonoBehaviour();
                    if (monoBehaviour != null)
                    {
                        monoBehaviour.StopCoroutine(_resumeCoroutine);
                    }
                    _resumeCoroutine = null;
                }

                _barSiblingIndexCache.Clear();
                _cachedDistances.Clear();
                _sortedActiveBars.Clear();
                _lastKnownDistances.Clear();
                _barStates.Clear();

                _proximityStaminaManagerInstance = null;
                _wasInCleanMode = false;
                _resumeTime = 0f;
                _isProcessingResume = false;
                ClearCaches();

                // Сбрасываем кэшированные компоненты
                _cachedVerticalLayoutGroup = null;
                _cachedContentSizeFitter = null;
                _cachedMonoBehaviour = null;

                if (_isPeakStatsPaused)
                {
                    _isPeakStatsPaused = false;
                }
            }
            catch { }
        }

        /// <summary>
        /// Очистка всех ресурсов
        /// </summary>
        public void Cleanup()
        {
            try
            {
                StopUpdateCoroutine();

                if (_resumeCoroutine != null)
                {
                    try
                    {
                        MonoBehaviour monoBehaviour = GetCachedMonoBehaviour();
                        if (monoBehaviour != null)
                        {
                            monoBehaviour.StopCoroutine(_resumeCoroutine);
                        }
                    }
                    catch { }
                    _resumeCoroutine = null;
                }

                try
                {
                    ActivateAllPeakStatsBarsBeforeCleanup();
                }
                catch { }

                _isPeakStatsPaused = false;
                _wasInCleanMode = false;
                _resumeTime = 0f;
                _isProcessingResume = false;
                _lastKnownDistances.Clear();
                _barStates.Clear();
                IsInitialized = false;

                _proximityStaminaManagerInstance = null;
                _barSiblingIndexCache.Clear();
                _sortedActiveBars.Clear();
                _cachedDistances.Clear();

                _proximityStaminaManagerType = null;
                _characterStaminaBarType = null;
                _entryType = null;

                _staminaBarsField = null;
                _disabledStaminaBarsField = null;
                _observedCharacterProperty = null;
                _animateDisableMethod = null;
                _animateEnableMethod = null;
                _isEnabledField = null;
                _animateEnableDisableCoroutineField = null;

                ClearCaches();

                // Сбрасываем кэшированные компоненты
                _cachedVerticalLayoutGroup = null;
                _cachedContentSizeFitter = null;
                _cachedMonoBehaviour = null;

                try
                {
                    var harmony = new Harmony("com.yourname.SelectiveHider.PeakStatsIntegration");
                    harmony.UnpatchSelf();
                }
                catch { }
            }
            catch { }
        }

        /// <summary>
        /// Принудительно активирует все бары PeakStats перед очисткой
        /// </summary>
        private void ActivateAllPeakStatsBarsBeforeCleanup()
        {
            try
            {
                if (_proximityStaminaManagerInstance == null)
                {
                    FindAndCacheProximityStaminaManager();
                }

                if (_proximityStaminaManagerInstance == null)
                    return;

                var staminaBars = _staminaBarsField?.GetValue(_proximityStaminaManagerInstance) as IDictionary;
                if (staminaBars == null)
                    return;

                foreach (DictionaryEntry entry in staminaBars)
                {
                    var character = entry.Key as Character;
                    var staminaBar = entry.Value;

                    if (character == null || staminaBar == null || character.IsLocal)
                        continue;

                    var barGameObject = GetGameObjectFromBar(staminaBar);
                    if (barGameObject == null)
                        continue;

                    if (!barGameObject.activeSelf && _animateEnableMethod != null)
                    {
                        try
                        {
                            SafeInvokeAnimateEnable(staminaBar, character);
                        }
                        catch { }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Получает кэшированный MonoBehaviour (оптимизация)
        /// </summary>
        private MonoBehaviour GetCachedMonoBehaviour()
        {
            try
            {
                if (_cachedMonoBehaviour != null && _cachedMonoBehaviour.gameObject != null &&
                    _cachedMonoBehaviour.gameObject.activeInHierarchy)
                {
                    return _cachedMonoBehaviour;
                }

                // Ищем CharacterStaminaBar
                if (_characterStaminaBarType != null)
                {
                    var allCharacterBars = UnityEngine.Object.FindObjectsOfType(_characterStaminaBarType);
                    if (allCharacterBars.Length > 0 && allCharacterBars[0] is MonoBehaviour mono)
                    {
                        if (mono != null && mono.gameObject != null && mono.gameObject.activeInHierarchy)
                        {
                            _cachedMonoBehaviour = mono;
                            return mono;
                        }
                    }
                }

                // Ищем любой активный MonoBehaviour (ограничиваем поиск)
                var allMonoBehaviours = UnityEngine.Object.FindObjectsOfType<MonoBehaviour>();
                int checkLimit = Mathf.Min(3, allMonoBehaviours.Length);

                for (int i = 0; i < checkLimit; i++)
                {
                    var mono = allMonoBehaviours[i];
                    if (mono != null && mono.gameObject != null && mono.gameObject.activeInHierarchy)
                    {
                        _cachedMonoBehaviour = mono;
                        return mono;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}