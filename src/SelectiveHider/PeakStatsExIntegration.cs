using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace SelectiveHider.Patches
{
    /// <summary>
    /// Интеграция с модом PeakStatsEx: скрывает бары стамины тиммейтов в чистом режиме.
    /// Бары нельзя выключать через SetActive - их OnDisable сбрасывает состояние (isEnabled, масштаб, корутины),
    /// поэтому прячем их через CanvasGroup.alpha, а сами бары продолжают работать как обычно.
    /// </summary>
    public class PeakStatsExIntegration
    {
        private const string PeakStatsExNamespace = "PeakStatsEx";
        private const string CharacterStaminaBarTypeName = "PeakStatsEx.MonoBehaviours.CharacterStaminaBar";

        private static readonly ManualLogSource _logger = BepInEx.Logging.Logger.CreateLogSource("PeakStatsExIntegration");

        // Бар -> (его CanvasGroup, исходная прозрачность)
        private static readonly Dictionary<MonoBehaviour, KeyValuePair<CanvasGroup, float>> _hiddenBars =
            new Dictionary<MonoBehaviour, KeyValuePair<CanvasGroup, float>>();

        private static Func<bool> _shouldHide;
        private Harmony _harmony;

        public bool IsInitialized { get; private set; }

        /// <summary>
        /// Инициализация. Вызывать после загрузки всех плагинов (например, в Start).
        /// </summary>
        public void Initialize(Func<bool> shouldHide)
        {
            try
            {
                Assembly peakStatsExAssembly = FindPeakStatsExAssembly();
                if (peakStatsExAssembly == null)
                {
                    _logger.LogInfo("PeakStatsEx не найден. Интеграция отключена.");
                    return;
                }

                Type barType = peakStatsExAssembly.GetType(CharacterStaminaBarTypeName);
                MethodInfo updateMethod = barType != null ? AccessTools.Method(barType, "Update") : null;
                if (updateMethod == null)
                {
                    _logger.LogWarning("Не удалось найти CharacterStaminaBar.Update в PeakStatsEx");
                    return;
                }

                _shouldHide = shouldHide;
                _harmony = new Harmony("nixi.SelectiveHider.PeakStatsExIntegration");
                _harmony.Patch(updateMethod,
                    postfix: new HarmonyMethod(typeof(PeakStatsExIntegration), nameof(CharacterStaminaBarUpdatePostfix)));

                IsInitialized = true;
                _logger.LogInfo("PeakStatsEx интеграция успешно инициализирована");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка инициализации PeakStatsEx интеграции: {ex}");
                IsInitialized = false;
            }
        }

        private static Assembly FindPeakStatsExAssembly()
        {
            foreach (var pluginInfo in Chainloader.PluginInfos.Values)
            {
                Type pluginType = pluginInfo?.Instance?.GetType();
                if (pluginType != null && pluginType.Namespace == PeakStatsExNamespace)
                    return pluginType.Assembly;
            }

            return null;
        }

        // Update выполняется только у активных баров, поэтому новые бары подхватываются автоматически
        private static void CharacterStaminaBarUpdatePostfix(MonoBehaviour __instance)
        {
            try
            {
                if (_shouldHide != null && _shouldHide())
                    Hide(__instance);
                else if (_hiddenBars.Count > 0)
                    RestoreAll();
            }
            catch (Exception ex)
            {
                _logger.LogDebug($"CharacterStaminaBarUpdatePostfix: {ex.Message}");
            }
        }

        private static void Hide(MonoBehaviour bar)
        {
            if (bar == null) return;

            if (!_hiddenBars.TryGetValue(bar, out var entry) || entry.Key == null)
            {
                CanvasGroup canvasGroup = bar.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = bar.gameObject.AddComponent<CanvasGroup>();

                entry = new KeyValuePair<CanvasGroup, float>(canvasGroup, canvasGroup.alpha);
                _hiddenBars[bar] = entry;
            }

            if (entry.Key.alpha != 0f)
                entry.Key.alpha = 0f;
        }

        /// <summary>
        /// Возвращает видимость всем скрытым барам
        /// </summary>
        public static void RestoreAll()
        {
            foreach (var kvp in _hiddenBars)
            {
                if (kvp.Value.Key != null)
                    kvp.Value.Key.alpha = kvp.Value.Value;
            }
            _hiddenBars.Clear();
        }

        public void Cleanup()
        {
            try
            {
                RestoreAll();
                _harmony?.UnpatchSelf();
                _harmony = null;
                _shouldHide = null;
                IsInitialized = false;
            }
            catch { }
        }
    }
}
