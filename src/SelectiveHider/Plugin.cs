using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using System.ComponentModel;
using System.Reflection;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

namespace SelectiveHider
{
    [BepInPlugin("com.yourname.SelectiveHider", "Selective HUD Hider", "1.0.1")]
    public class SelectiveHiderPlugin : BaseUnityPlugin
    {
        private static class TransparencyManager
        {
            private static Dictionary<GameObject, CanvasGroup> _canvasGroups = new Dictionary<GameObject, CanvasGroup>();
            private static Dictionary<GameObject, float> _originalAlphas = new Dictionary<GameObject, float>();

            public static void SetTransparent(GameObject obj, bool transparent)
            {
                if (obj == null || !obj) return;

                try
                {
                    if (transparent)
                    {
                        // Создаем или получаем CanvasGroup
                        if (!_canvasGroups.TryGetValue(obj, out var canvasGroup) || canvasGroup == null)
                        {
                            canvasGroup = obj.GetComponent<CanvasGroup>();
                            if (canvasGroup == null)
                                canvasGroup = obj.AddComponent<CanvasGroup>();

                            _canvasGroups[obj] = canvasGroup;
                        }

                        // Сохраняем исходную прозрачность
                        if (!_originalAlphas.ContainsKey(obj))
                            _originalAlphas[obj] = canvasGroup.alpha;

                        // Устанавливаем прозрачность
                        canvasGroup.alpha = 0f;
                        canvasGroup.blocksRaycasts = false;
                        canvasGroup.interactable = false;
                    }
                    else
                    {
                        // Восстанавливаем прозрачность
                        if (_canvasGroups.TryGetValue(obj, out var canvasGroup) && canvasGroup != null)
                        {
                            if (_originalAlphas.TryGetValue(obj, out float originalAlpha))
                                canvasGroup.alpha = originalAlpha;
                            else
                                canvasGroup.alpha = 1f;

                            canvasGroup.blocksRaycasts = true;
                            canvasGroup.interactable = true;
                        }
                        else
                        {
                            // Если CanvasGroup не нашли, проверяем стандартный
                            var existingCanvasGroup = obj.GetComponent<CanvasGroup>();
                            if (existingCanvasGroup != null)
                                existingCanvasGroup.alpha = 1f;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[TransparencyManager] Ошибка: {ex.Message}");
                }
            }

            public static void Cleanup()
            {
                // Восстанавливаем все прозрачности перед очисткой
                foreach (var kvp in _canvasGroups)
                {
                    if (kvp.Key != null && kvp.Value != null)
                    {
                        if (_originalAlphas.TryGetValue(kvp.Key, out float originalAlpha))
                            kvp.Value.alpha = originalAlpha;
                        else
                            kvp.Value.alpha = 1f;

                        kvp.Value.blocksRaycasts = true;
                        kvp.Value.interactable = true;
                    }
                }

                _canvasGroups.Clear();
                _originalAlphas.Clear();
            }

            private static ManualLogSource Logger => BepInEx.Logging.Logger.CreateLogSource("TransparencyManager");
        }

        #region ИСПРАВЛЕННЫЙ CustomStaminaStats

        private static class CustomStaminaStats
        {
            // Настройки
            private static bool _enabled = true;
            private static bool _showPercent = false;
            private static int _fontSize = 16;
            private static float _outlineThickness = 0.15f;

            // Текстовые элементы
            private static Dictionary<CharacterAfflictions.STATUSTYPE, TextMeshProUGUI> _statusTexts = new Dictionary<CharacterAfflictions.STATUSTYPE, TextMeshProUGUI>();
            private static TextMeshProUGUI _staminaText;
            private static Canvas _canvas;

            // Кэшированные ссылки
            private static Character _observedCharacter;
            private static StaminaBar _staminaBar;
            private static Dictionary<CharacterAfflictions.STATUSTYPE, BarAffliction> _cachedAfflictions = new Dictionary<CharacterAfflictions.STATUSTYPE, BarAffliction>();

            // Для отслеживания изменений
            private static float _lastStaminaValue = -1f;
            private static Dictionary<CharacterAfflictions.STATUSTYPE, float> _lastStatusValues = new Dictionary<CharacterAfflictions.STATUSTYPE, float>();

            // Цвета для статусов
            private static readonly Dictionary<CharacterAfflictions.STATUSTYPE, Color> StatusColors = new Dictionary<CharacterAfflictions.STATUSTYPE, Color>
            {
                { CharacterAfflictions.STATUSTYPE.Injury, new Color(1f, 0.3f, 0f) },
                { CharacterAfflictions.STATUSTYPE.Hunger, new Color(0.9f, 0.6f, 0.1f) },
                { CharacterAfflictions.STATUSTYPE.Cold, new Color(0.2f, 0.6f, 0.9f) },
                { CharacterAfflictions.STATUSTYPE.Poison, new Color(0.6f, 0.1f, 0.6f) },
                { CharacterAfflictions.STATUSTYPE.Crab, new Color(0.8805f, 0.2077f, 0.2579f) },
                { CharacterAfflictions.STATUSTYPE.Curse, new Color(0.5f, 0.1f, 0.5f) },
                { CharacterAfflictions.STATUSTYPE.Drowsy, new Color(1f, 0.4f, 0.8f) },
                { CharacterAfflictions.STATUSTYPE.Weight, new Color(0.75f, 0.55f, 0.25f) },
                { CharacterAfflictions.STATUSTYPE.Hot, new Color(1f, 0.3f, 0.1f) },
                { CharacterAfflictions.STATUSTYPE.Thorns, new Color(0.4f, 0.5f, 0f) },
                { CharacterAfflictions.STATUSTYPE.Spores, new Color(0.6f, 0.35f, 0.4f) },
                { CharacterAfflictions.STATUSTYPE.Web, new Color(0.9f, 0.9f, 0.9f) }
            };

            // Шрифт игры
            private static TMP_FontAsset _gameFont;
            private static Material _textMaterial;

            // Путь к AssetBundle со шрифтом
            private static string _fontBundlePath = null;

            // Метод для установки пути к AssetBundle (вызов из SelectiveHiderPlugin)
            public static void SetFontBundlePath(string path)
            {
                _fontBundlePath = path;
                _gameFont = null; // Сбрасываем кэш, чтобы при следующем вызове загрузился новый шрифт
                Logger.LogInfo($"[CustomStaminaStats] Путь к шрифту установлен: {path}");
            }

            // Для мгновенного скрытия/показа
            private static bool _forceHidden = false;

            // Инициализация
            public static void Initialize(bool enabled, bool showPercent, int fontSize, float outline)
            {
                _enabled = enabled;
                _showPercent = showPercent;
                _fontSize = fontSize;
                _outlineThickness = outline;

                FindGameTextStyle();
                CreateCanvas();
                Logger.LogInfo("[CustomStaminaStats] Инициализирован");
            }

            // Метод для очистки только кэша цифр (без уничтожения Canvas и шрифта)
            public static void ResetCache()
            {
                try
                {
                    Logger.LogInfo("[CustomStaminaStats] Очистка кэша цифр...");

                    // Очищаем текстовые элементы статусов
                    foreach (var kvp in _statusTexts.ToList())
                    {
                        if (kvp.Value != null && kvp.Value.gameObject != null)
                        {
                            // Только очищаем текст, не уничтожаем объект
                            kvp.Value.text = "";
                        }
                    }

                    // Очищаем текст стамины
                    if (_staminaText != null && _staminaText.gameObject != null)
                    {
                        _staminaText.text = "";
                    }

                    // Сбрасываем кэшированные значения
                    _lastStaminaValue = -1f;
                    _lastStatusValues.Clear();

                    // Сбрасываем кэшированные ссылки на персонажей и полоски
                    _observedCharacter = null;
                    _staminaBar = null;
                    _cachedAfflictions.Clear();

                    // Сбрасываем принудительное скрытие
                    _forceHidden = false;

                    // Скрываем все тексты
                    HideAllTexts();

                    Logger.LogInfo("[CustomStaminaStats] Кэш цифр очищен");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[CustomStaminaStats] Ошибка очистки кэша: {ex.Message}");
                }
            }

            // Поиск игрового шрифта
            private static void FindGameTextStyle()
            {
                if (_gameFont != null) return; // КЭШ: если уже нашли шрифт, не ищем снова

                try
                {
                    // ТОЛЬКО наш кастомный шрифт Chewy из AssetBundle
                    if (_fontBundlePath != null)
                    {
                        string bundlePath = System.IO.Path.Combine(_fontBundlePath, "chewy_font");
                        if (System.IO.File.Exists(bundlePath))
                        {
                            AssetBundle fontBundle = AssetBundle.LoadFromFile(bundlePath);
                            if (fontBundle != null)
                            {
                                _gameFont = fontBundle.LoadAsset<TMP_FontAsset>("Chewy_Regular_SDF");
                                fontBundle.Unload(false);
                                if (_gameFont != null)
                                {
                                    Logger.LogInfo("[CustomStaminaStats] Загружен кастомный шрифт Chewy");
                                    return; // Успех - выходим
                                }
                            }
                        }
                        // Если дошли сюда - наш шрифт не загрузился
                        Logger.LogWarning("[CustomStaminaStats] Не удалось загрузить кастомный шрифт");
                    }

                    // Fallback 1: Игровой шрифт (как у тебя)
                    var textComponents = Resources.FindObjectsOfTypeAll<TextMeshProUGUI>();
                    foreach (var textComponent in textComponents)
                    {
                        if (textComponent.name == "InteractNameText" ||
                            textComponent.name == "InteractPromptText" ||
                            textComponent.name == "ItemPromptMain")
                        {
                            _gameFont = textComponent.font;
                            _textMaterial = textComponent.material;
                            Logger.LogInfo($"[CustomStaminaStats] Используем игровой шрифт: {_gameFont?.name}");
                            return;
                        }
                    }

                    // Fallback 2: Любой доступный шрифт (последний резерв)
                    var fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
                    if (fonts.Length > 0)
                    {
                        _gameFont = fonts[0];
                        Logger.LogInfo($"[CustomStaminaStats] Используем первый доступный шрифт: {_gameFont.name}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[CustomStaminaStats] Ошибка поиска шрифта: {ex.Message}");
                }
            }

            // Настройка стиля текста
            private static void SetupGameTextStyle(TextMeshProUGUI text, int fontSize, TextAlignmentOptions alignment)
            {
                if (text == null || text.gameObject == null) return;

                // 1. Базовые свойства
                text.fontSize = fontSize;
                text.alignment = alignment;
                text.textWrappingMode = TextWrappingModes.NoWrap;
                text.overflowMode = TextOverflowModes.Overflow;
                text.color = Color.white;
                text.raycastTarget = false;
                text.fontStyle = FontStyles.Bold;

                // 2. Шрифт (загружаем один раз)
                if (_gameFont == null)
                {
                    FindGameTextStyle();
                }
                text.font = _gameFont;

                // 3. МАТЕРИАЛ: Проверяем, не создан ли уже наш материал
                bool needsNewMaterial = true;

                if (text.fontMaterial != null && text.fontMaterial.name.EndsWith("_IsolatedMaterial"))
                {
                    // Материал уже наш - переиспользуем
                    needsNewMaterial = false;
                }
                else if (text.fontMaterial != null)
                {
                    // Чужой материал - уничтожаем старый перед созданием нового
                    UnityEngine.Object.DestroyImmediate(text.fontMaterial);
                }

                if (needsNewMaterial && text.font != null && text.font.material != null)
                {
                    try
                    {
                        Material newMaterial = new Material(text.font.material);
                        newMaterial.name = $"{text.gameObject.name}_IsolatedMaterial";
                        text.fontMaterial = newMaterial;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError($"[CustomStaminaStats] Ошибка создания материала: {ex.Message}");
                    }
                }

                // 4. Outline
                text.outlineWidth = _outlineThickness;
                text.outlineColor = Color.black;

                // 5. Тень (добавляем только один раз)
                Shadow shadow = text.gameObject.GetComponent<Shadow>();
                if (shadow == null)
                {
                    shadow = text.gameObject.AddComponent<Shadow>();
                    shadow.effectColor = new Color(0f, 0f, 0f, 0.95f);
                    shadow.effectDistance = new Vector2(2f, -2f);
                }
            }

            private static void RemoveDestroyedReferences()
            {
                try
                {
                    // 1. Очистка _statusTexts
                    var keysToRemoveStatus = new List<CharacterAfflictions.STATUSTYPE>();
                    foreach (var kvp in _statusTexts)
                    {
                        // Важно: двойная проверка для Unity объектов
                        if (kvp.Value == null || kvp.Value.gameObject == null || !kvp.Value.gameObject)
                            keysToRemoveStatus.Add(kvp.Key);
                    }

                    foreach (var key in keysToRemoveStatus)
                    {
                        _statusTexts.Remove(key);
                        _lastStatusValues.Remove(key);
                    }

                    // 2. Очистка _cachedAfflictions
                    var keysToRemoveAfflictions = new List<CharacterAfflictions.STATUSTYPE>();
                    foreach (var kvp in _cachedAfflictions)
                    {
                        if (kvp.Value == null || kvp.Value.gameObject == null || !kvp.Value.gameObject)
                            keysToRemoveAfflictions.Add(kvp.Key);
                    }

                    foreach (var key in keysToRemoveAfflictions)
                    {
                        _cachedAfflictions.Remove(key);
                    }

                    // 3. Проверка _staminaText
                    if (_staminaText != null && (_staminaText.gameObject == null || !_staminaText.gameObject))
                    {
                        _staminaText = null;
                        _lastStaminaValue = -1f;
                    }

                    // 4. Проверка _canvas
                    if (_canvas != null && (_canvas.gameObject == null || !_canvas.gameObject))
                    {
                        _canvas = null;
                    }

                    // 5. Проверка _observedCharacter (игровой объект)
                    if (_observedCharacter != null && _observedCharacter.gameObject == null)
                    {
                        _observedCharacter = null;
                    }

                    // 6. Проверка _staminaBar
                    if (_staminaBar != null && (_staminaBar.gameObject == null || !_staminaBar.gameObject))
                    {
                        _staminaBar = null;
                    }

                    Logger.LogInfo($"[CustomStaminaStats] Очищено {keysToRemoveStatus.Count} статусов и {keysToRemoveAfflictions.Count} аффликций");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[CustomStaminaStats] Ошибка в RemoveDestroyedReferences: {ex.Message}");
                }
            }

            private static void SetupTMPOutline(TextMeshProUGUI text, float outlineThickness)
            {
                if (text.fontMaterial == null) return;

                try
                {
                    // Для шейдеров TextMeshPro/Distance Field
                    text.fontMaterial.EnableKeyword("OUTLINE_ON");

                    // Устанавливаем толщину outline через свойство материала
                    text.fontMaterial.SetFloat("_OutlineWidth", outlineThickness);

                    // Устанавливаем цвет outline
                    text.fontMaterial.SetColor("_OutlineColor", Color.black);

                    // Также устанавливаем через свойства TMP (для совместимости)
                    text.outlineWidth = outlineThickness;
                    text.outlineColor = Color.black;

                    // Форсируем обновление
                    text.fontMaterial.renderQueue = 3000; // Стандартный render queue для UI
                    text.SetMaterialDirty();
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[CustomStaminaStats] Ошибка настройки outline: {ex.Message}");

                    // Fallback: используем только свойства TMP
                    text.outlineWidth = outlineThickness;
                    text.outlineColor = Color.black;
                }
            }

            private static float _lastCleanupTime = 0f;
            private static readonly float CLEANUP_INTERVAL = 5f; // Раз в 5 секунд

            // Обновление всех статусов
            public static void UpdateAll()
            {
                // Очищаем мертвые ссылки не каждый кадр, а раз в 5 секунд
                if (Time.time - _lastCleanupTime >= CLEANUP_INTERVAL)
                {
                    RemoveDestroyedReferences();
                    _lastCleanupTime = Time.time;
                }

                if (!_enabled || _canvas == null) return;

                try
                {
                    // Проверяем принудительное скрытие
                    if (_forceHidden)
                    {
                        if (_canvas.gameObject.activeSelf)
                            _canvas.gameObject.SetActive(false);
                        return;
                    }

                    if (!_canvas.gameObject.activeSelf)
                        _canvas.gameObject.SetActive(true);

                    // Находим текущего персонажа
                    if (_observedCharacter == null || _observedCharacter != Character.observedCharacter)
                    {
                        _observedCharacter = Character.observedCharacter;
                        if (_observedCharacter == null)
                        {
                            HideAllTexts();
                            return;
                        }
                    }

                    // Находим StaminaBar
                    if (_staminaBar == null)
                    {
                        var gui = GUIManager.instance;
                        if (gui != null && gui.bar != null)
                        {
                            _staminaBar = gui.bar;
                            CacheAfflictions();
                        }
                    }

                    // Обновляем тексты
                    UpdateStaminaText();
                    UpdateStatusTexts();
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[CustomStaminaStats] Ошибка обновления: {ex.Message}");
                }
            }

            // Кэширование аффликций
            private static void CacheAfflictions()
            {
                _cachedAfflictions.Clear();
                if (_staminaBar == null || _staminaBar.afflictions == null) return;

                foreach (var affliction in _staminaBar.afflictions)
                {
                    if (affliction != null)
                    {
                        _cachedAfflictions[affliction.afflictionType] = affliction;
                    }
                }
            }

            // Создание Canvas
            private static void CreateCanvas()
            {
                if (_canvas != null) return;

                var canvasObj = new GameObject("CustomStaminaStatsCanvas");
                GameObject.DontDestroyOnLoad(canvasObj);
                _canvas = canvasObj.AddComponent<Canvas>();
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                _canvas.sortingOrder = 100;
                _canvas.gameObject.SetActive(false); // Начинаем скрытым

                var scaler = canvasObj.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

                canvasObj.AddComponent<GraphicRaycaster>();
                canvasObj.layer = LayerMask.NameToLayer("UI");

                Logger.LogInfo("[CustomStaminaStats] Canvas создан");
            }

            // Создание текстового элемента
            private static TextMeshProUGUI CreateTextElement(string name, Transform parent, Vector2 anchoredPosition,
                TextAlignmentOptions alignment = TextAlignmentOptions.Center, int fontSize = -1)
            {
                if (fontSize == -1) fontSize = _fontSize;

                var textObj = new GameObject(name);
                textObj.transform.SetParent(parent, false);

                var text = textObj.AddComponent<TextMeshProUGUI>();
                var rect = text.GetComponent<RectTransform>();

                // Настройка RectTransform
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.sizeDelta = new Vector2(80, 30);
                rect.anchoredPosition = anchoredPosition;

                // Настройка стиля текста
                SetupGameTextStyle(text, fontSize, alignment);

                return text;
            }

            // Обновление текста выносливости
            private static void UpdateStaminaText()
            {
                if (_observedCharacter == null || _staminaBar == null || _staminaBar.staminaBar == null)
                {
                    HideStaminaText();
                    return;
                }

                try
                {
                    // Проверяем, нужно ли показывать
                    bool shouldShow = !_observedCharacter.data.fullyPassedOut &&
                                     !_observedCharacter.data.dead &&
                                     _staminaBar.staminaBar.gameObject.activeSelf;

                    if (!shouldShow)
                    {
                        HideStaminaText();
                        return;
                    }

                    // Получаем значения стамины
                    float currentStamina = _observedCharacter.data.currentStamina * 100f;
                    float maxStamina = _observedCharacter.GetMaxStamina() * 100f;

                    // Создаем текст при первом вызове
                    if (_staminaText == null)
                    {
                        Vector2 tempPos = new Vector2(0f, 0f);
                        _staminaText = CreateTextElement("CustomStaminaText", _canvas.transform, tempPos,
                            TextAlignmentOptions.Center, _fontSize + 2);
                    }

                    // Обновляем только если значение изменилось
                    if (Math.Abs(currentStamina - _lastStaminaValue) > 0.1f || _staminaText.text == "")
                    {
                        _lastStaminaValue = currentStamina;

                        _staminaText.text = _showPercent ? $"{Math.Round(currentStamina, 0):F0}%" : $"{Math.Round(currentStamina, 0):F0}";

                        // Цвет в зависимости от уровня выносливости
                        float ratio = currentStamina / maxStamina;
                        if (ratio < 0.25f)
                            _staminaText.color = new Color(1f, 0.3f, 0.3f);
                        else if (ratio < 0.5f)
                            _staminaText.color = new Color(1f, 0.92f, 0.016f);
                        else
                            _staminaText.color = new Color(0.5f, 1f, 0.5f);
                    }

                    // Позиционируем текст (по центру полоски стамины)
                    PositionStaminaText();

                    // Показываем текст
                    _staminaText.gameObject.SetActive(true);
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[CustomStaminaStats] Ошибка обновления выносливости: {ex.Message}");
                }
            }

            // Позиционирование текста стамины
            private static void PositionStaminaText()
            {
                if (_staminaBar == null || _staminaBar.staminaBar == null || _staminaText == null) return;

                try
                {
                    var staminaBarRect = _staminaBar.staminaBar.transform as RectTransform;
                    if (staminaBarRect == null) return;

                    // Получаем мировую позицию центра полоски стамины
                    var worldCenter = staminaBarRect.TransformPoint(staminaBarRect.rect.center);

                    // Конвертируем в экранные координаты
                    var screenPos = RectTransformUtility.WorldToScreenPoint(_canvas.worldCamera, worldCenter);

                    // Позиционируем ПОД полоской
                    screenPos.y -= 30f;

                    // Конвертируем в координаты Canvas
                    Vector2 canvasPos;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _canvas.transform as RectTransform,
                        screenPos,
                        _canvas.worldCamera,
                        out canvasPos))
                    {
                        _staminaText.rectTransform.anchoredPosition = canvasPos;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[CustomStaminaStats] Ошибка позиционирования стамины: {ex.Message}");
                }
            }

            // Обновление текстов статусов
            private static void UpdateStatusTexts()
            {
                if (_observedCharacter == null || _staminaBar == null)
                {
                    HideAllStatusTexts();
                    return;
                }

                try
                {
                    var afflictions = _observedCharacter.refs.afflictions;
                    if (afflictions == null) return;

                    // Проходим по всем типам статусов
                    foreach (CharacterAfflictions.STATUSTYPE statusType in Enum.GetValues(typeof(CharacterAfflictions.STATUSTYPE)))
                    {
                        float value = afflictions.GetCurrentStatus(statusType) * 100f;

                        if (value > 0f)
                        {
                            // Ищем BarAffliction
                            BarAffliction targetAffliction = null;
                            if (!_cachedAfflictions.TryGetValue(statusType, out targetAffliction) || targetAffliction == null)
                            {
                                foreach (var affliction in _staminaBar.afflictions)
                                {
                                    if (affliction != null && affliction.gameObject.activeSelf && affliction.afflictionType == statusType)
                                    {
                                        targetAffliction = affliction;
                                        _cachedAfflictions[statusType] = affliction;
                                        break;
                                    }
                                }
                            }

                            if (targetAffliction == null || !targetAffliction.gameObject.activeSelf)
                            {
                                if (_statusTexts.ContainsKey(statusType))
                                {
                                    _statusTexts[statusType].gameObject.SetActive(false);
                                }
                                continue;
                            }

                            // Получаем текущее значение
                            float lastValue = 0f;
                            _lastStatusValues.TryGetValue(statusType, out lastValue);

                            // Создаем или получаем текстовый элемент
                            if (!_statusTexts.TryGetValue(statusType, out var text) || text == null)
                            {
                                Vector2 tempPos = new Vector2(0f, -200f);
                                text = CreateTextElement($"Custom{statusType}Text", _canvas.transform, tempPos);

                                if (StatusColors.TryGetValue(statusType, out var color))
                                    text.color = color;

                                _statusTexts[statusType] = text;
                            }

                            // Обновляем текст если значение изменилось
                            if (Math.Abs(value - lastValue) > 0.1f || text.text == "")
                            {
                                _lastStatusValues[statusType] = value;
                                text.text = _showPercent ? $"{Math.Round(value, 0):F0}%" : $"{Math.Round(value, 0):F0}";
                            }

                            text.gameObject.SetActive(true);

                            // Позиционируем (по центру полоски статуса)
                            PositionStatusText(statusType, text, targetAffliction);
                        }
                        else if (_statusTexts.ContainsKey(statusType))
                        {
                            _statusTexts[statusType].gameObject.SetActive(false);
                            _lastStatusValues[statusType] = 0f;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[CustomStaminaStats] Ошибка обновления статусов: {ex.Message}");
                }
            }

            // Позиционирование текста статуса
            private static void PositionStatusText(CharacterAfflictions.STATUSTYPE statusType, TextMeshProUGUI text, BarAffliction affliction)
            {
                if (affliction == null || text == null) return;

                try
                {
                    var afflictionRect = affliction.transform as RectTransform;
                    if (afflictionRect == null) return;

                    // Получаем мировую позицию центра полоски статуса
                    var worldCenter = afflictionRect.TransformPoint(afflictionRect.rect.center);

                    // Конвертируем в экранные координаты
                    var screenPos = RectTransformUtility.WorldToScreenPoint(_canvas.worldCamera, worldCenter);

                    // Позиционируем ПОД полоской
                    screenPos.y -= 25f;

                    // Конвертируем в координаты Canvas
                    Vector2 canvasPos;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        _canvas.transform as RectTransform,
                        screenPos,
                        _canvas.worldCamera,
                        out canvasPos))
                    {
                        text.rectTransform.anchoredPosition = canvasPos;
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[CustomStaminaStats] Ошибка позиционирования статуса {statusType}: {ex.Message}");
                }
            }

            // Скрытие всех текстов
            private static void HideAllTexts()
            {
                if (_staminaText != null)
                    _staminaText.gameObject.SetActive(false);

                foreach (var text in _statusTexts.Values)
                {
                    if (text != null)
                        text.gameObject.SetActive(false);
                }
            }

            private static void HideStaminaText()
            {
                if (_staminaText != null)
                    _staminaText.gameObject.SetActive(false);
            }

            private static void HideAllStatusTexts()
            {
                foreach (var text in _statusTexts.Values)
                {
                    if (text != null)
                        text.gameObject.SetActive(false);
                }
            }

            // Очистка
            public static void Cleanup()
            {
                try
                {
                    foreach (var text in _statusTexts.Values)
                    {
                        if (text != null)
                        {
                            if (text.material != null)
                                UnityEngine.Object.Destroy(text.material);
                            UnityEngine.Object.Destroy(text.gameObject);
                        }
                    }
                    _statusTexts.Clear();
                    _lastStatusValues.Clear();

                    if (_staminaText != null)
                    {
                        if (_staminaText.material != null)
                            UnityEngine.Object.Destroy(_staminaText.material);
                        UnityEngine.Object.Destroy(_staminaText.gameObject);
                        _staminaText = null;
                    }

                    if (_canvas != null)
                    {
                        UnityEngine.Object.Destroy(_canvas.gameObject);
                        _canvas = null;
                    }

                    _observedCharacter = null;
                    _staminaBar = null;
                    _cachedAfflictions.Clear();
                    _textMaterial = null;
                    _lastStaminaValue = -1f;
                    _forceHidden = false;

                    Logger.LogInfo("[CustomStaminaStats] Очищено");
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[CustomStaminaStats] Ошибка очистки: {ex.Message}");
                }
            }

            // Установка видимости (МГНОВЕННАЯ)
            public static void SetVisible(bool visible)
            {
                try
                {
                    _forceHidden = !visible;

                    if (_canvas != null)
                    {
                        _canvas.gameObject.SetActive(visible);

                        // Мгновенное обновление
                        if (visible)
                        {
                            Canvas.ForceUpdateCanvases();

                            // Обновляем все тексты сразу
                            UpdateAll();

                            // Принудительно показываем все элементы
                            if (_staminaText != null)
                                _staminaText.gameObject.SetActive(true);

                            foreach (var text in _statusTexts.Values)
                            {
                                if (text != null)
                                    text.gameObject.SetActive(true);
                            }
                        }
                        else
                        {
                            // Мгновенно скрываем все
                            HideAllTexts();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"[CustomStaminaStats] Ошибка установки видимости: {ex.Message}");
                }
            }

            // Обновление настроек
            public static void UpdateSettings(bool enabled, bool showPercent, int fontSize, float outline)
            {
                _enabled = enabled;
                _showPercent = showPercent;
                _fontSize = fontSize;
                _outlineThickness = outline;

                // Обновляем все тексты
                UpdateAllTextsStyle();
            }

            // Обновление стиля всех текстов
            private static void UpdateAllTextsStyle()
            {
                // Основная стамина
                if (_staminaText != null)
                {
                    _staminaText.fontSize = _fontSize + 2;
                    SetupTMPOutline(_staminaText, _outlineThickness);
                }

                // Статусы
                foreach (var text in _statusTexts.Values)
                {
                    if (text != null)
                    {
                        text.fontSize = _fontSize;
                        SetupTMPOutline(text, _outlineThickness);
                    }
                }
            }

            private static ManualLogSource Logger => BepInEx.Logging.Logger.CreateLogSource("CustomStaminaStats");
        }

        #endregion

        #region Конфигурационные переменные

        private static HashSet<string> _transparencyElements = new HashSet<string>
        {
            "MoraleBoost",
            "ConnectionLog",
            "Spectating",
        };

        public enum ToggleKey
        {
            [Description("F1")] F1,
            [Description("F2")] F2,
            [Description("F3")] F3,
            [Description("F4")] F4,
            [Description("F5")] F5,
            [Description("F6")] F6,
            [Description("F7")] F7,
            [Description("F8")] F8,
            [Description("F9")] F9,
            [Description("F10")] F10,
            [Description("F11")] F11,
            [Description("F12")] F12,
            [Description("Insert")] Insert,
            [Description("Delete")] Delete,
            [Description("Home")] Home,
            [Description("End")] End,
            [Description("Page Up")] PageUp,
            [Description("Page Down")] PageDown,
            [Description("` (BackQuote)")] BackQuote,
            [Description("/ (Slash)")] Slash,
            [Description("\\ (Backslash)")] Backslash,
            [Description("Alpha1 (1)")] Alpha1,
            [Description("Alpha2 (2)")] Alpha2,
            [Description("Alpha3 (3)")] Alpha3,
            [Description("Alpha4 (4)")] Alpha4,
            [Description("Alpha5 (5)")] Alpha5,
            [Description("Alpha6 (6)")] Alpha6,
            [Description("Alpha7 (7)")] Alpha7,
            [Description("Alpha8 (8)")] Alpha8,
            [Description("Alpha9 (9)")] Alpha9,
            [Description("Alpha0 (0)")] Alpha0,
            [Description("Minus (-)")] Minus,
            [Description("Equals (=)")] Equals,
            [Description("Backspace")] Backspace,
            [Description("Tab")] Tab,
            [Description("Caps Lock")] CapsLock,
            [Description("Left Shift")] LeftShift,
            [Description("Right Shift")] RightShift,
            [Description("Left Ctrl")] LeftControl,
            [Description("Right Ctrl")] RightControl,
            [Description("Left Alt")] LeftAlt,
            [Description("Right Alt")] RightAlt,
            [Description("Space")] Space,
            [Description("Enter")] Return,
            [Description("Escape")] Escape,
            [Description("Print Screen")] Print,
            [Description("Scroll Lock")] ScrollLock,
            [Description("Pause")] Pause,
            [Description("Num Lock")] Numlock,
            [Description("NumPad 0")] Keypad0,
            [Description("NumPad 1")] Keypad1,
            [Description("NumPad 2")] Keypad2,
            [Description("NumPad 3")] Keypad3,
            [Description("NumPad 4")] Keypad4,
            [Description("NumPad 5")] Keypad5,
            [Description("NumPad 6")] Keypad6,
            [Description("NumPad 7")] Keypad7,
            [Description("NumPad 8")] Keypad8,
            [Description("NumPad 9")] Keypad9,
            [Description("NumPad .")] KeypadPeriod,
            [Description("NumPad /")] KeypadDivide,
            [Description("NumPad *")] KeypadMultiply,
            [Description("NumPad -")] KeypadMinus,
            [Description("NumPad +")] KeypadPlus,
            [Description("NumPad Enter")] KeypadEnter,
            [Description("NumPad =")] KeypadEquals,
            [Description("Up Arrow")] UpArrow,
            [Description("Down Arrow")] DownArrow,
            [Description("Left Arrow")] LeftArrow,
            [Description("Right Arrow")] RightArrow
        }

        // Основные настройки
        private ConfigEntry<ToggleKey> _toggleKeyConfig;
        private ConfigEntry<float> _checkIntervalConfig;
        private ConfigEntry<bool> _letterboxConfig;
        private GameObject _cachedCanvasLetterbox;

        // Основные настройки HUD
        private ConfigEntry<bool> _barGroupConfig;
        private ConfigEntry<bool> _barGroupMushroomsConfig;
        private ConfigEntry<bool> _inventoryConfig;
        private ConfigEntry<bool> _promptsConfig;
        private ConfigEntry<bool> _useItemConfig;
        private ConfigEntry<bool> _useItemFriendTFConfig;
        private ConfigEntry<bool> _dayNightTextConfig;
        private ConfigEntry<bool> _reticlesConfig;
        private ConfigEntry<bool> _spectatingConfig;
        private ConfigEntry<bool> _heroConfig;
        private ConfigEntry<bool> _theFogRisesConfig;
        private ConfigEntry<bool> _theLavaRisesConfig;
        private ConfigEntry<bool> _endgameConfig;
        private ConfigEntry<bool> _connectionLogConfig;
        private ConfigEntry<bool> _ascentUIConfig;
        private ConfigEntry<bool> _timerHeightUIConfig;

        // Настройки Custom Stamina Stats
        private ConfigEntry<bool> _customStaminaEnabled;
        private ConfigEntry<bool> _customStaminaShowPercent;
        private ConfigEntry<int> _customStaminaFontSize;
        private ConfigEntry<float> _customStaminaOutline;

        // EXTRA CANVAS
        private ConfigEntry<bool> _canvasBetterPingDistanceConfig;
        private ConfigEntry<bool> _canvasPassedOutMarkersConfig;

        private HashSet<string> _managedElements = new HashSet<string>();
        private static Dictionary<string, GameObject> _cachedExtraCanvases = new Dictionary<string, GameObject>();
        private static Dictionary<string, bool> _extraCanvasOriginalStates = new Dictionary<string, bool>();

        #endregion

        #region Основные переменные

        private static HashSet<string> _currentlyHidden = new HashSet<string>();
        private static Dictionary<string, bool> _originalStates = new Dictionary<string, bool>();
        private static bool _isCleanModeActive = false;
        private static GameObject _cachedCanvasHUD;
        private static GameObject _cachedCanvasHero;
        private static float _nextCheckTime = 0f;
        private KeyCode _currentToggleKey;
        private float _currentCheckInterval;
        private bool _configsDirty = true;
        private static Harmony _harmony;

        #endregion

        private void Awake()
        {
            Logger.LogInfo($"Мод {Info.Metadata.Name} v1.0.0 загружен!");
            InitializeConfig();

            // Устанавливаем путь к AssetBundle со шрифтом
            string pluginPath = System.IO.Path.GetDirectoryName(Info.Location);
            CustomStaminaStats.SetFontBundlePath(pluginPath);

            _harmony = new Harmony(Info.Metadata.GUID);

            // Подписка на смену сцены для очистки кэша
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

            // Высокая частота обновления (60 кадров в секунду)
            InvokeRepeating(nameof(UpdateCustomStaminaStats), 0.5f, 0.0167f);
        }

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            Logger.LogInfo($"Сцена загружена: {scene.name}");

            // Очищаем кэшированные ссылки плагина
            _cachedCanvasHUD = null;
            _cachedCanvasHero = null;
            _cachedCanvasLetterbox = null;
            _cachedExtraCanvases.Clear();
            _originalStates.Clear();
            _currentlyHidden.Clear();
            _extraCanvasOriginalStates.Clear();

            ForceShowCustomStaminaOnGameScenes(scene);

            // Сбрасываем состояние чистого режима если он активен
            if (_isCleanModeActive)
            {
                _isCleanModeActive = false;
                Logger.LogInfo("Сброшен чистый режим из-за смены сцены");
            }

            // Очищаем CustomStaminaStats при загрузке сцены Airport (конец игры) или Title
            if (scene.name == "Airport" || scene.name == "Title")
            {
                ClearCustomStaminaCache();
            }
        }

        private void ClearCustomStaminaCache()
        {
            try
            {
                Logger.LogInfo("Очистка кэша CustomStaminaStats (сцена Airport)");

                // Мы не можем напрямую очистить приватные поля CustomStaminaStats,
                // поэтому нам нужно добавить публичный метод очистки в CustomStaminaStats

                // Вызываем метод очистки кэша в CustomStaminaStats
                CustomStaminaStats.ResetCache();

                // Также очищаем наш внутренний кэш
                if (_customStaminaEnabled.Value)
                {
                    // Принудительно обновляем настройки (без пересоздания Canvas)
                    UpdateCustomStaminaSettings();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка очистки кэша CustomStaminaStats: {ex.Message}");
            }
        }

        private bool IsFogOrLavaElement(string elementName)
        {
            return elementName == "TheFogRises" || elementName == "TheLavaRises";
        }

        private void HandleAnimatedElements(bool hide)
        {
            try
            {
                // Обработка TheFogRises и TheLavaRises(отдельно, чтобы избежать проблем с анимациями)
                if (_theFogRisesConfig.Value)
                {
                    Transform fogRises = FindInCanvasHUD("TheFogRises", true);
                    if (fogRises != null && fogRises.gameObject.activeSelf)
                    {
                        fogRises.gameObject.SetActive(false);
                        _currentlyHidden.Add("TheFogRises");
                        Logger.LogInfo("TheFogRises скрыт");
                    }
                }

                if (_theLavaRisesConfig.Value)
                {
                    Transform lavaRises = FindInCanvasHUD("TheLavaRises", true);
                    if (lavaRises != null && lavaRises.gameObject.activeSelf)
                    {
                        lavaRises.gameObject.SetActive(false);
                        _currentlyHidden.Add("TheLavaRises");
                        Logger.LogInfo("TheLavaRises скрыт");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка обработки анимированных элементов: {ex.Message}");
            }
        }

        private void UpdateCustomStaminaStats()
        {
            // Добавляем проверку чтобы не падать если объект уничтожен
            try
            {
                if (_customStaminaEnabled.Value)
                {
                    CustomStaminaStats.UpdateAll();
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"UpdateCustomStaminaStats error: {ex.Message}");
                // Отменяем повторяющийся вызов если проблема
                CancelInvoke(nameof(UpdateCustomStaminaStats));
            }
        }

        private void InitializeConfig()
        {
            try
            {
                // Основные настройки
                _toggleKeyConfig = Config.Bind("General", "Toggle Key", ToggleKey.F1,
                    new ConfigDescription("Клавиша для переключения Чистого режима.", null, Array.Empty<object>()));

                _checkIntervalConfig = Config.Bind("General", "Check Interval", 1f,
                    new ConfigDescription("Частота проверок в чистом режиме (в секундах).",
                        new AcceptableValueRange<float>(0.5f, 3f), Array.Empty<object>()));

                // Настройка Letterbox
                _letterboxConfig = Config.Bind("General", "Letterbox", false,
                    "Enable Canvas_Letterbox (black bars) in Clean Mode");

                // Основные настройки HUD
                _barGroupConfig = Config.Bind("Main HUD", "BarGroup", true,
                    "Hide health bars, hunger, thirst, etc.");
                _barGroupMushroomsConfig = Config.Bind("Main HUD", "BarGroup_Mushrooms", false,
                    "Hide mushroom strips (if any)");
                _inventoryConfig = Config.Bind("Main HUD", "Inventory", true,
                    "Hide inventory");
                _promptsConfig = Config.Bind("Main HUD", "Prompts", true,
                    "Hide hints (E - take, F - use, etc.)");
                _useItemConfig = Config.Bind("Main HUD", "UseItem", true,
                    "Hide the use of items");
                _useItemFriendTFConfig = Config.Bind("Main HUD", "UseItemFriendTF", false,
                    "Hide the use of items on friends");
                _dayNightTextConfig = Config.Bind("Main HUD", "DayNightText", true,
                    "Hide the day/night text");
                _reticlesConfig = Config.Bind("Main HUD", "Reticles", true,
                    "Hide sights in the center of the screen");
                _spectatingConfig = Config.Bind("Main HUD", "Spectating", true,
                    "Hide the switching interface behind players at death");
                _heroConfig = Config.Bind("Main HUD", "Hero", true,
                    "Hide the name interface at the beginning of the biome");
                _theFogRisesConfig = Config.Bind("Main HUD", "TheFogRises", true,
                    "Hide the message about the rising fog");
                _theLavaRisesConfig = Config.Bind("Main HUD", "TheLavaRises", true,
                    "Hide a message about rising lava");
                _endgameConfig = Config.Bind("Main HUD", "Endgame", true,
                    "Hide the end-of-game interface");
                _connectionLogConfig = Config.Bind("Main HUD", "ConnectionLog", true,
                    "Hide the connection log of other players");
                _ascentUIConfig = Config.Bind("Main HUD", "AscentUI", true,
                    "Hide the interface of the ascension number");
                _timerHeightUIConfig = Config.Bind("Main HUD", "Timer & Height UI", true,
                    "Hide timer and height (mod PeakStats)");

                // Настройки Custom Stamina Stats
                _customStaminaEnabled = Config.Bind("Custom Stamina Stats", "Enabled", false,
                    "Enable digital statuses and endurance values");
                _customStaminaShowPercent = Config.Bind("Custom Stamina Stats", "ShowPercentage", false,
                    "Show the percentage sign (%) after the values");
                _customStaminaFontSize = Config.Bind("Custom Stamina Stats", "FontSize", 16,
                    new ConfigDescription("Font Size",
                        new AcceptableValueRange<int>(10, 32), Array.Empty<object>()));
                _customStaminaOutline = Config.Bind("Custom Stamina Stats", "OutlineThickness", 0.15f,
                    new ConfigDescription("The thickness of the text outline",
                        new AcceptableValueRange<float>(0.05f, 0.5f), Array.Empty<object>()));

                // EXTRA CANVAS
                _canvasBetterPingDistanceConfig = Config.Bind("Extra Canvas", "Canvas_BetterPingDistance", false,
                    "Hide distances from ping (mod BetterPingDistance)");
                _canvasPassedOutMarkersConfig = Config.Bind("Extra Canvas", "Canvas_PassedOutMarkers", false,
                    "Hide the markers of players who have lost consciousness (mod DownedAwareness)");

                // Инициализируем CustomStaminaStats
                CustomStaminaStats.Initialize(
                    _customStaminaEnabled.Value,
                    _customStaminaShowPercent.Value,
                    _customStaminaFontSize.Value,
                    _customStaminaOutline.Value
                );

                // Обработчики изменений настроек
                _barGroupConfig.SettingChanged += MarkConfigsDirty;
                _barGroupMushroomsConfig.SettingChanged += MarkConfigsDirty;
                _inventoryConfig.SettingChanged += MarkConfigsDirty;
                _promptsConfig.SettingChanged += MarkConfigsDirty;
                _useItemConfig.SettingChanged += MarkConfigsDirty;
                _useItemFriendTFConfig.SettingChanged += MarkConfigsDirty;
                _dayNightTextConfig.SettingChanged += MarkConfigsDirty;
                _reticlesConfig.SettingChanged += MarkConfigsDirty;
                _spectatingConfig.SettingChanged += MarkConfigsDirty;
                _heroConfig.SettingChanged += MarkConfigsDirty;
                _theFogRisesConfig.SettingChanged += MarkConfigsDirty;
                _theLavaRisesConfig.SettingChanged += MarkConfigsDirty;
                _endgameConfig.SettingChanged += MarkConfigsDirty;
                _connectionLogConfig.SettingChanged += MarkConfigsDirty;
                _ascentUIConfig.SettingChanged += MarkConfigsDirty;
                _timerHeightUIConfig.SettingChanged += MarkConfigsDirty;

                _customStaminaEnabled.SettingChanged += (s, e) => UpdateCustomStaminaSettings();
                _customStaminaShowPercent.SettingChanged += (s, e) => UpdateCustomStaminaSettings();
                _customStaminaFontSize.SettingChanged += (s, e) => UpdateCustomStaminaSettings();
                _customStaminaOutline.SettingChanged += (s, e) => UpdateCustomStaminaSettings();
                _letterboxConfig.SettingChanged += (s, e) =>
                {
                    Logger.LogInfo($"Конфиг Letterbox изменен: {_letterboxConfig.Value}");

                    // Принудительно обновляем состояние если в чистом режиме
                    if (_isCleanModeActive)
                    {
                        ManageCanvasLetterbox(_letterboxConfig.Value);
                    }
                };

                _toggleKeyConfig.SettingChanged += (s, e) =>
                    _currentToggleKey = (KeyCode)Enum.Parse(typeof(KeyCode), _toggleKeyConfig.Value.ToString());

                _checkIntervalConfig.SettingChanged += (s, e) =>
                    _currentCheckInterval = _checkIntervalConfig.Value;

                _canvasBetterPingDistanceConfig.SettingChanged += OnExtraCanvasConfigChanged;
                _canvasPassedOutMarkersConfig.SettingChanged += OnExtraCanvasConfigChanged;

                _currentToggleKey = (KeyCode)Enum.Parse(typeof(KeyCode), _toggleKeyConfig.Value.ToString());
                _currentCheckInterval = _checkIntervalConfig.Value;

                UpdateManagedElements();
                _configsDirty = false;

                Logger.LogInfo("Конфигурация загружена! Custom Stamina Stats готов!");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка инициализации: {ex}");
            }
        }

        private void UpdateCustomStaminaSettings()
        {
            // Очищаем старый кэш перед обновлением настроек
            if (_customStaminaEnabled.Value)
            {
                CustomStaminaStats.ResetCache();
            }

            CustomStaminaStats.UpdateSettings(
                _customStaminaEnabled.Value,
                _customStaminaShowPercent.Value,
                _customStaminaFontSize.Value,
                _customStaminaOutline.Value
            );
        }

        private void MarkConfigsDirty(object sender = null, EventArgs e = null)
        {
            _configsDirty = true;
        }

        private void OnExtraCanvasConfigChanged(object sender, EventArgs e)
        {
            ConfigEntry<bool> configEntry = sender as ConfigEntry<bool>;
            if (configEntry != null)
            {
                string canvasName = configEntry.Definition.Key;
                bool newValue = configEntry.Value;

                Logger.LogInfo($"Конфиг {canvasName} изменен: {newValue}");

                if (!newValue && _isCleanModeActive)
                {
                    RestoreExtraCanvas(canvasName);
                }
            }
        }

        private void OnDestroy()
        {
            try
            {
                CustomStaminaStats.Cleanup();
                CancelInvoke(nameof(UpdateCustomStaminaStats));

                // Безопасная отписка от события смены сцены
                try
                {
                    UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
                }
                catch (Exception ex)
                {
                    Logger.LogWarning($"Ошибка отписки от sceneLoaded: {ex.Message}");
                }

                if (_harmony != null)
                {
                    _harmony.UnpatchSelf();
                    _harmony = null;
                }

                Logger.LogInfo("SelectiveHiderPlugin выгружен");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка в OnDestroy: {ex.Message}");
            }
        }

        private void UpdateManagedElements()
        {
            _managedElements.Clear();

            if (_barGroupConfig.Value) _managedElements.Add("BarGroup");
            if (_barGroupMushroomsConfig.Value) _managedElements.Add("BarGroup_Mushrooms");
            if (_inventoryConfig.Value) _managedElements.Add("Inventory");
            if (_promptsConfig.Value) _managedElements.Add("Prompts");
            if (_useItemConfig.Value) _managedElements.Add("UseItem");
            if (_useItemFriendTFConfig.Value) _managedElements.Add("UseItemFriendTF");
            if (_dayNightTextConfig.Value) _managedElements.Add("DayNightText");
            if (_reticlesConfig.Value) _managedElements.Add("Reticles");
            if (_spectatingConfig.Value) _managedElements.Add("Spectating");
            if (_heroConfig.Value) _managedElements.Add("Hero");
            if (_theFogRisesConfig.Value) _managedElements.Add("TheFogRises");
            if (_theLavaRisesConfig.Value) _managedElements.Add("TheLavaRises");
            if (_endgameConfig.Value) _managedElements.Add("Endgame");
            if (_connectionLogConfig.Value) _managedElements.Add("ConnectionLog");
            if (_ascentUIConfig.Value) _managedElements.Add("AscentUI");
            if (_timerHeightUIConfig.Value) _managedElements.Add("Timer & Height UI");
        }

        private void Update()
        {
            if (_configsDirty)
            {
                UpdateManagedElements();
                _configsDirty = false;
            }

            if (Input.GetKeyDown(_currentToggleKey))
            {
                ToggleHudMode();
            }

            if (_isCleanModeActive && Time.time >= _nextCheckTime)
            {
                EnforceCleanMode();
                _nextCheckTime = Time.time + _currentCheckInterval;
            }
        }

        private void ToggleHudMode()
        {
            try
            {
                _isCleanModeActive = !_isCleanModeActive;

                if (_isCleanModeActive)
                {
                    Logger.LogInfo("=== АКТИВАЦИЯ ЧИСТОГО РЕЖИМА ===");
                    // Сбрасываем кэш при переключении режимов
                    _cachedCanvasLetterbox = null;
                    ApplyCleanMode();
                }
                else
                {
                    Logger.LogInfo("=== ВЫХОД ИЗ ЧИСТОГО РЕЖИМА ===");
                    ApplyNormalMode();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка переключения: {ex.Message}");
                _isCleanModeActive = false;
                ApplyNormalMode();
            }
        }

        private GameObject FindExtraCanvas(string canvasName)
        {
            if (_cachedExtraCanvases.TryGetValue(canvasName, out GameObject cachedCanvas) && cachedCanvas != null)
            {
                return cachedCanvas;
            }

            GameObject canvas = GameObject.Find(canvasName);
            if (canvas != null)
            {
                _cachedExtraCanvases[canvasName] = canvas;
            }

            return canvas;
        }

        private void HideExtraCanvas(string canvasName, ConfigEntry<bool> config)
        {
            if (!config.Value) return;

            GameObject canvas = FindExtraCanvas(canvasName);
            if (canvas != null && canvas.activeSelf)
            {
                if (!_extraCanvasOriginalStates.ContainsKey(canvasName))
                {
                    _extraCanvasOriginalStates[canvasName] = canvas.activeSelf;
                }

                canvas.SetActive(false);
                _currentlyHidden.Add(canvasName);
            }
        }

        private void RestoreExtraCanvas(string canvasName)
        {
            GameObject canvas = FindExtraCanvas(canvasName);
            if (canvas != null)
            {
                if (_extraCanvasOriginalStates.TryGetValue(canvasName, out bool originalState))
                {
                    canvas.SetActive(originalState);
                }
                else
                {
                    canvas.SetActive(true);
                }

                _currentlyHidden.Remove(canvasName);
            }
        }

        private void RestoreAllExtraCanvases()
        {
            foreach (string canvasName in _currentlyHidden.ToList())
            {
                if (canvasName.StartsWith("Canvas_"))
                {
                    RestoreExtraCanvas(canvasName);
                }
            }
        }

        private void ApplyCleanMode()
        {
            try
            {
                _currentlyHidden.Clear();
                _originalStates.Clear();
                _extraCanvasOriginalStates.Clear();
                TransparencyManager.Cleanup(); // Очищаем предыдущие прозрачности

                // Сбрасываем кэш для Canvas_Letterbox
                _cachedCanvasLetterbox = null;

                // Находим Canvas_HUD
                _cachedCanvasHUD = GameObject.Find("Canvas_HUD");
                if (_cachedCanvasHUD == null)
                {
                    Logger.LogWarning("Canvas_HUD не найден!");
                    return;
                }

                // Скрываем элементы Canvas_HUD согласно конфигурации
                foreach (string elementName in _managedElements)
                {
                    // Пропускаем TheFogRises и TheLavaRises - они обрабатываются отдельно
                    if (IsFogOrLavaElement(elementName)) continue;
                    // Пропускаем MoraleBoost, он обрабатывается отдельно
                    if (elementName == "MoraleBoost") continue;

                    Transform child = FindInCanvasHUD(elementName, true);
                    if (child == null) continue;

                    // Для анимированных элементов используем прозрачность
                    if (_transparencyElements.Contains(elementName))
                    {
                        // ОСОБЫЙ СЛУЧАЙ: Spectating - не сохраняем его состояние (как ConnectionLog)
                        if (elementName != "Spectating" && elementName != "ConnectionLog")
                        {
                            // Сохраняем исходное состояние только для тех элементов, которые не управляются игрой
                            if (!_originalStates.ContainsKey(elementName))
                                _originalStates[elementName] = child.gameObject.activeSelf;
                        }

                        TransparencyManager.SetTransparent(child.gameObject, true);
                        _currentlyHidden.Add(elementName + "_transparent");

                        // Сохраняем исходное состояние
                        if (!_originalStates.ContainsKey(elementName))
                            _originalStates[elementName] = child.gameObject.activeSelf;
                    }
                    else // Для остальных - обычное скрытие
                    {
                        if (!_originalStates.ContainsKey(elementName))
                            _originalStates[elementName] = child.gameObject.activeSelf;

                        if (child.gameObject.activeSelf)
                        {
                            child.gameObject.SetActive(false);
                            _currentlyHidden.Add(elementName);
                        }
                    }
                }

                // Обработка TheFogRises и TheLavaRises (отдельно, чтобы избежать проблем с анимациями)
                HandleAnimatedElements(true);

                // Особый случай: MoraleBoost находится внутри BarGroup
                if (_barGroupConfig.Value && _managedElements.Contains("BarGroup"))
                {
                    Transform barGroup = _cachedCanvasHUD.transform.Find("BarGroup");
                    if (barGroup != null)
                    {
                        Transform moraleBoost = barGroup.Find("Bar/MoraleBoost");
                        if (moraleBoost != null)
                        {
                            TransparencyManager.SetTransparent(moraleBoost.gameObject, true);
                            _currentlyHidden.Add("MoraleBoost_transparent");
                        }
                    }
                }

                // Управляем CustomStaminaStats в зависимости от настроек BarGroup
                if (_barGroupConfig.Value)
                {
                    // Если BarGroup = true, скрываем StaminaStats вместе с BarGroup
                    CustomStaminaStats.SetVisible(false);
                    _originalStates["CustomStaminaStats"] = true;
                }
                else
                {
                    // Если BarGroup = false, StaminaStats не скрываем
                    _originalStates["CustomStaminaStats"] = true;
                }

                // Скрываем внешние Canvas если конфиги включены
                HideExtraCanvas("Canvas_BetterPingDistance", _canvasBetterPingDistanceConfig);
                HideExtraCanvas("Canvas_PassedOutMarkers", _canvasPassedOutMarkersConfig);

                // Скрываем Canvas_Hero
                ManageCanvasHero(true);

                // Управляем Canvas_Letterbox (включаем в чистом режиме)
                ManageCanvasLetterbox(true);

                // Мгновенное обновление
                Canvas.ForceUpdateCanvases();

                Logger.LogInfo($"Чистый режим активирован. Скрыто элементов: {_currentlyHidden.Count}");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка ApplyCleanMode: {ex.Message}");
            }
        }

        private void ApplyNormalMode()
        {
            try
            {
                // Очищаем записи о TheFogRises и TheLavaRises (они должны оставаться неактивными)
                _currentlyHidden.Remove("TheFogRises");
                _currentlyHidden.Remove("TheLavaRises");

                // Восстанавливаем Canvas_HUD элементы
                if (_cachedCanvasHUD != null)
                {
                    foreach (Transform child in _cachedCanvasHUD.transform)
                    {
                        if (child == null) continue;

                        string childName = child.name;

                        // Для прозрачных элементов - восстанавливаем видимость
                        if (_currentlyHidden.Contains(childName + "_transparent"))
                        {
                            // ОСОБЫЙ СЛУЧАЙ: Spectating и ConnectionLog - просто убираем прозрачность
                            // Не восстанавливаем активность, так как игрой
                            TransparencyManager.SetTransparent(child.gameObject, false);

                            // Удаляем из скрытых
                            _currentlyHidden.Remove(childName + "_transparent");
                        }
                        // Для обычных элементов - восстанавливаем активность
                        else if (_originalStates.TryGetValue(childName, out bool originalState))
                        {
                            // Для Spectating и ConnectionLog не восстанавливаем активность (их управляет игра)
                            if (childName == "Spectating" || childName == "ConnectionLog")
                            {
                                // Пропускаем - игрой
                                _currentlyHidden.Remove(childName);
                                continue;
                            }

                            if (child.gameObject.activeSelf != originalState)
                            {
                                child.gameObject.SetActive(originalState);
                            }
                        }
                    }
                }

                // Восстанавливаем Canvas_Hero
                ManageCanvasHero(false);

                // Особый случай: MoraleBoost
                if (_currentlyHidden.Contains("MoraleBoost_transparent"))
                {
                    Transform barGroup = _cachedCanvasHUD?.transform.Find("BarGroup");
                    Transform moraleBoost = barGroup?.Find("Bar/MoraleBoost");
                    if (moraleBoost != null)
                    {
                        TransparencyManager.SetTransparent(moraleBoost.gameObject, false);
                    }
                }

                // Восстанавливаем прозрачность Canvas_Hero
                if (_heroConfig.Value && _cachedCanvasHero != null)
                {
                    TransparencyManager.SetTransparent(_cachedCanvasHero, false);
                }

                // Восстанавливаем Canvas_Letterbox
                ManageCanvasLetterbox(false);

                // Восстанавливаем все внешние Canvas
                RestoreAllExtraCanvases();

                // Восстанавливаем CustomStaminaStats
                if (_customStaminaEnabled.Value)
                {
                    bool wasHiddenByBarGroup = _barGroupConfig.Value &&
                                               _originalStates.ContainsKey("CustomStaminaStats") &&
                                               _originalStates["CustomStaminaStats"];

                    if (!_barGroupConfig.Value || wasHiddenByBarGroup)
                    {
                        CustomStaminaStats.SetVisible(true);
                    }
                    else
                    {
                        CustomStaminaStats.SetVisible(false);
                    }
                }
                else
                {
                    CustomStaminaStats.SetVisible(false);
                }

                // Очищаем временные данные
                _currentlyHidden.Clear();
                _originalStates.Clear();
                _extraCanvasOriginalStates.Clear();
                _cachedCanvasHUD = null;
                _cachedCanvasHero = null;
                _cachedCanvasLetterbox = null; // Сбрасываем кэш
                _cachedExtraCanvases.Clear();
                // Очищаем прозрачности
                TransparencyManager.Cleanup();

                // Мгновенное обновление
                Canvas.ForceUpdateCanvases();

                Logger.LogInfo("Обычный режим восстановлен!");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка ApplyNormalMode: {ex.Message}");
            }
        }

        // Метод для поиска Canvas_Letterbox (включая неактивные)
        [Obsolete]

        private GameObject GetCanvasLetterbox()
        {
            if (_cachedCanvasLetterbox != null)
                return _cachedCanvasLetterbox;

            // Ищем среди всех объектов, включая неактивные
            Canvas[] allCanvases = FindObjectsOfType<Canvas>(true);
            foreach (Canvas canvas in allCanvases)
            {
                if (canvas != null && canvas.name == "Canvas_Letterbox")
                {
                    _cachedCanvasLetterbox = canvas.gameObject;
                    Logger.LogInfo($"Найден Canvas_Letterbox (активен: {_cachedCanvasLetterbox.activeSelf})");
                    return _cachedCanvasLetterbox;
                }
            }

            // Альтернативный поиск через трансформы
            GameObject[] allObjects = Resources.FindObjectsOfTypeAll<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (obj != null && obj.name == "Canvas_Letterbox")
                {
                    _cachedCanvasLetterbox = obj;
                    Logger.LogInfo($"Найден Canvas_Letterbox через общий поиск (активен: {_cachedCanvasLetterbox.activeSelf})");
                    return _cachedCanvasLetterbox;
                }
            }

            Logger.LogWarning("Canvas_Letterbox не найден!");
            return null;
        }

        private void ManageCanvasLetterbox(bool enableInCleanMode)
        {
            if (!_letterboxConfig.Value) return;

            GameObject canvasLetterbox = GetCanvasLetterbox();
            if (canvasLetterbox == null) return;

            try
            {
                if (_isCleanModeActive)
                {
                    // Включаем Canvas_Letterbox в чистом режиме
                    if (enableInCleanMode)
                    {
                        // Запоминаем исходное состояние при первом включении
                        if (!_originalStates.ContainsKey("Canvas_Letterbox"))
                        {
                            _originalStates["Canvas_Letterbox"] = canvasLetterbox.activeSelf;
                            Logger.LogInfo($"Запомнено исходное состояние Canvas_Letterbox: {canvasLetterbox.activeSelf}");
                        }

                        // Включаем Canvas_Letterbox
                        if (!canvasLetterbox.activeSelf)
                        {
                            canvasLetterbox.SetActive(true);
                            _currentlyHidden.Add("Canvas_Letterbox");
                            Logger.LogInfo("Canvas_Letterbox активирован в чистом режиме");
                        }
                    }
                }
                else
                {
                    // Восстанавливаем исходное состояние при выходе из чистого режима
                    if (_originalStates.TryGetValue("Canvas_Letterbox", out bool originalState))
                    {
                        if (canvasLetterbox.activeSelf != originalState)
                        {
                            canvasLetterbox.SetActive(originalState);
                            Logger.LogInfo($"Canvas_Letterbox восстановлен в исходное состояние: {originalState}");
                        }

                        // Удаляем из списка скрытых
                        _currentlyHidden.Remove("Canvas_Letterbox");
                    }
                    else
                    {
                        // Если не запомнили исходное состояние, просто выключаем
                        if (canvasLetterbox.activeSelf)
                        {
                            canvasLetterbox.SetActive(false);
                            _currentlyHidden.Remove("Canvas_Letterbox");
                            Logger.LogInfo("Canvas_Letterbox выключен (исходное состояние не найдено)");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка управления Canvas_Letterbox: {ex.Message}");
            }
        }

        private void ForceShowCustomStaminaOnGameScenes(Scene scene)
        {
            // Простая проверка: если это игровая сцена (Level_*) и CustomStaminaStats включен
            if (scene.name.StartsWith("Level_") && _customStaminaEnabled.Value)
            {
                CustomStaminaStats.SetVisible(true);
            }
        }

        private GameObject GetCanvasHero()
        {
            if (_cachedCanvasHero == null)
                _cachedCanvasHero = GameObject.Find("Canvas_Hero");
            return _cachedCanvasHero;
        }

        private void ManageCanvasHero(bool hide)
        {
            if (!_heroConfig.Value) return;

            GameObject canvasHero = GetCanvasHero();
            if (canvasHero == null) return;

            try
            {
                if (hide)
                {
                    // Сохраняем исходное состояние только если еще не сохраняли
                    if (!_originalStates.ContainsKey("Canvas_Hero"))
                    {
                        _originalStates["Canvas_Hero"] = canvasHero.activeSelf;
                        Logger.LogInfo($"Запомнено состояние Canvas_Hero: {canvasHero.activeSelf}");
                    }

                    // Скрываем только если активен
                    if (canvasHero.activeSelf)
                    {
                        canvasHero.SetActive(false);
                        _currentlyHidden.Add("Canvas_Hero");
                        Logger.LogInfo("Canvas_Hero скрыт");
                    }
                }
                else
                {
                    // Восстанавливаем исходное состояние если оно было сохранено
                    if (_originalStates.TryGetValue("Canvas_Hero", out bool originalState))
                    {
                        if (canvasHero.activeSelf != originalState)
                        {
                            canvasHero.SetActive(originalState);
                            Logger.LogInfo($"Canvas_Hero восстановлен в состояние: {originalState}");
                        }
                    }
                    else
                    {
                        // Если состояние не сохранено, просто включаем
                        if (!canvasHero.activeSelf)
                        {
                            canvasHero.SetActive(true);
                            Logger.LogInfo("Canvas_Hero включен (состояние не найдено)");
                        }
                    }

                    // Удаляем из скрытых
                    _currentlyHidden.Remove("Canvas_Hero");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError($"Ошибка управления Canvas_Hero: {ex.Message}");
            }
        }

        private Transform FindInCanvasHUD(string name, bool includeInactive = true)
        {
            if (_cachedCanvasHUD == null) return null;

            // Сначала пробуем стандартный поиск (для активных объектов)
            Transform child = _cachedCanvasHUD.transform.Find(name);
            if (child != null) return child;

            // Если не нашли и разрешено искать неактивные
            if (includeInactive)
            {
                // Ищем среди всех дочерних объектов (включая неактивные)
                // Используем GetComponentsInChildren с параметром true
                Transform[] allChildren = _cachedCanvasHUD.GetComponentsInChildren<Transform>(true);
                foreach (Transform t in allChildren)
                {
                    if (t != null && t.name == name)
                    {
                        return t;
                    }
                }
            }

            return null;
        }

        //todo: PEAKStats [Unity] NullReferenceException: Object reference not set to an instance of an object

        private void EnforceCleanMode()
        {
            if (!_isCleanModeActive) return;

            try
            {
                // Убеждаемся что CustomStaminaStats скрыт если BarGroup = true
                if (_barGroupConfig.Value)
                {
                    CustomStaminaStats.SetVisible(false);
                }

                // Проверяем Canvas_HUD
                if (_cachedCanvasHUD == null)
                    _cachedCanvasHUD = GameObject.Find("Canvas_HUD");

                // Проверяем Canvas_HUD
                if (_cachedCanvasHUD != null)
                {
                    foreach (string elementName in _managedElements)
                    {
                        // Пропускаем TheFogRises и TheLavaRises - они обрабатываются отдельно
                        if (IsFogOrLavaElement(elementName)) continue;
                        // Пропускаем MoraleBoost, он обрабатывается отдельно
                        if (elementName == "MoraleBoost") continue;

                        Transform child = FindInCanvasHUD(elementName, true);
                        if (child == null) continue;

                        // Для прозрачных элементов проверяем прозрачность
                        if (_transparencyElements.Contains(elementName))
                        {
                            var canvasGroup = child.GetComponent<CanvasGroup>();
                            if (canvasGroup == null || canvasGroup.alpha > 0.01f)
                            {
                                TransparencyManager.SetTransparent(child.gameObject, true);
                                if (!_currentlyHidden.Contains(elementName + "_transparent"))
                                    _currentlyHidden.Add(elementName + "_transparent");

                                if (!_originalStates.ContainsKey(elementName))
                                    _originalStates[elementName] = child.gameObject.activeSelf;
                            }
                        }
                        // Для обычных элементов проверяем активность
                        else if (child.gameObject.activeSelf)
                        {
                            if (!_originalStates.ContainsKey(elementName))
                                _originalStates[elementName] = true;

                            child.gameObject.SetActive(false);
                            if (!_currentlyHidden.Contains(elementName))
                                _currentlyHidden.Add(elementName);
                        }
                    }
                }

                // Проверяем TheFogRises
                if (_theFogRisesConfig.Value)
                {
                    Transform fogRises = FindInCanvasHUD("TheFogRises", true);
                    if (fogRises != null && fogRises.gameObject.activeSelf)
                    {
                        fogRises.gameObject.SetActive(false);
                        if (!_currentlyHidden.Contains("TheFogRises"))
                            _currentlyHidden.Add("TheFogRises");
                    }
                }

                // Проверяем TheLavaRises
                if (_theLavaRisesConfig.Value)
                {
                    Transform lavaRises = FindInCanvasHUD("TheLavaRises", true);
                    if (lavaRises != null && lavaRises.gameObject.activeSelf)
                    {
                        lavaRises.gameObject.SetActive(false);
                        if (!_currentlyHidden.Contains("TheLavaRises"))
                            _currentlyHidden.Add("TheLavaRises");
                    }
                }

                // Проверяем Canvas_Hero
                if (_heroConfig.Value)
                {
                    GameObject canvasHero = GetCanvasHero();
                    if (canvasHero != null && canvasHero.activeSelf)
                    {
                        canvasHero.SetActive(false);
                        _currentlyHidden.Add("Canvas_Hero");
                    }
                }

                // Проверяем Canvas_Letterbox
                if (_letterboxConfig.Value)
                {
                    GameObject canvasLetterbox = GetCanvasLetterbox();
                    if (canvasLetterbox != null)
                    {
                        // Запоминаем исходное состояние если еще не запомнили
                        if (!_originalStates.ContainsKey("Canvas_Letterbox"))
                        {
                            _originalStates["Canvas_Letterbox"] = canvasLetterbox.activeSelf;
                        }

                        // Включаем если выключен
                        if (!canvasLetterbox.activeSelf)
                        {
                            canvasLetterbox.SetActive(true);
                            _currentlyHidden.Add("Canvas_Letterbox");
                        }
                    }
                }

                // Проверяем внешние Canvas
                if (_canvasBetterPingDistanceConfig.Value)
                {
                    GameObject canvas = FindExtraCanvas("Canvas_BetterPingDistance");
                    if (canvas != null && canvas.activeSelf)
                    {
                        canvas.SetActive(false);
                        _currentlyHidden.Add("Canvas_BetterPingDistance");
                    }
                }

                if (_canvasPassedOutMarkersConfig.Value)
                {
                    GameObject canvas = FindExtraCanvas("Canvas_PassedOutMarkers");
                    if (canvas != null && canvas.activeSelf)
                    {
                        canvas.SetActive(false);
                        _currentlyHidden.Add("Canvas_PassedOutMarkers");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Ошибка EnforceCleanMode: {ex.Message}");
            }
        }
    }
}