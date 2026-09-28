using System;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.UI;

namespace DarkFogSterilizer;

[BepInPlugin("org.fyyy.darkfogsterilizer", "DarkFogSterilizer", "0.1.0")]
[BepInIncompatibility("dsp.nebula-multiplayer")]
public sealed class Plugin : BaseUnityPlugin
{
    private ConfigEntry<KeyboardShortcut> _shortcut;
    private Action _pending;
    private Button _button;

    private void Awake()
    {
        _shortcut = Config.Bind("操作", "绝育星系", new KeyboardShortcut(KeyCode.X, KeyCode.LeftControl, KeyCode.LeftAlt),
            "在星图中选中恒星或行星后，打开绝育确认弹窗。");
        Logger.LogInfo("DarkFogSterilizer loaded. Use the starmap's 绝育星系 button or Ctrl+Alt+X.");
    }

    private void Update()
    {
        // Unity Update runs between completed simulation frames, including while the game is paused.
        if (_pending != null)
        {
            var action = _pending;
            _pending = null;
            action();
            return;
        }
        if (!GameMain.isRunning || GameMain.isLoading || DSPGame.IsMenuDemo) return;
        var map = UIRoot.instance.uiGame.starmap;
        if (!map.active) return;
        if (_button == null) CreateButton(map);
        _button.interactable = UIMessageBox.activeCount == 0;
        if (!VFInput.inputing && _shortcut.Value.IsDown()) Confirm();
    }

    private void CreateButton(UIStarmap map)
    {
        var go = new GameObject("dark-fog-sterilize", typeof(RectTransform), typeof(Image), typeof(Button));
        var rect = (RectTransform)go.transform;
        rect.SetParent(map.screenCanvas.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -50f);
        rect.sizeDelta = new Vector2(160f, 36f);

        var background = go.GetComponent<Image>();
        background.color = Color.white;
        _button = go.GetComponent<Button>();
        _button.targetGraphic = background;
        var colors = _button.colors;
        colors.normalColor = new Color(0.22f, 0.12f, 0.08f, 0.95f);
        colors.highlightedColor = new Color(0.45f, 0.24f, 0.12f, 1f);
        colors.pressedColor = new Color(0.65f, 0.32f, 0.12f, 1f);
        colors.selectedColor = colors.normalColor;
        _button.colors = colors;
        _button.onClick.AddListener(Confirm);

        var label = new GameObject("label", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
        label.rectTransform.SetParent(rect, false);
        label.rectTransform.anchorMin = Vector2.zero;
        label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = label.rectTransform.offsetMax = Vector2.zero;
        label.font = map.cursorViewText.font;
        label.fontSize = 18;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = new Color(1f, 0.8f, 0.6f, 1f);
        label.raycastTarget = false;
        label.text = "绝育星系";
    }

    private void OnDestroy()
    {
        if (_button != null) Destroy(_button.gameObject);
    }

    private void Confirm()
    {
        if (!GameMain.isRunning || GameMain.isLoading || DSPGame.IsMenuDemo ||
            UIMessageBox.activeCount > 0 || _pending != null) return;
        var map = UIRoot.instance.uiGame.starmap;
        if (!map.active) return;
        var star = map.focusStar != null ? map.focusStar.star :
            map.focusPlanet != null ? map.focusPlanet.planet.star :
            map.focusHive != null ? map.focusHive.hive.starData :
            map.viewStar ?? map.viewPlanet?.star ?? map.viewHive?.starData;
        if (star == null)
        {
            UIMessageBox.Show("绝育星系", "请先在星图中选中一颗恒星或行星。", "确定", UIMessageBox.INFO);
            return;
        }
        var data = GameMain.data;
        if (!data.spaceSector.isCombatMode)
        {
            UIMessageBox.Show("绝育星系", "当前存档未启用黑雾。", "确定", UIMessageBox.INFO);
            return;
        }
        int capacity = Sterilizer.Capacity(star);
        UIMessageBox.Show("绝育星系",
            $"目标：{star.displayName}（星系 ID：{star.id}）\n\n" +
            "清除该星系的黑雾地面基地、部队、中继站、太空建筑和火种，\n" +
            "同时清除已派往该星系的在途火种。\n" +
            $"按当前星系上限生成 {capacity} 个有物质、零能量的未完工中枢核心。\n\n" +
            "确认后立即执行，不自动保存或备份。",
            "取消", "确认绝育", UIMessageBox.WARNING, null,
            () => _pending = () => Execute(data, star, capacity));
    }

    private void Execute(GameData data, StarData star, int capacity)
    {
        if (!GameMain.isRunning || GameMain.isLoading || GameMain.data != data || Sterilizer.Capacity(star) != capacity)
        {
            UIMessageBox.Show("未执行绝育", "存档或星系上限已变化，请重新选择并确认。", "确定", UIMessageBox.INFO);
            return;
        }
        var ui = UIRoot.instance.uiGame;
        var map = ui.starmap;
        var viewedStar = map.viewStarSystem;
        try
        {
            if (map.viewHive?.starData == star)
                map.screenCameraController.SetViewTarget(null, star, null, null, VectorLF3.zero,
                    map.screenCameraController.dist, map.screenCameraController.dist, false, true);
            map.focusHive = null;
            map.focusEnemyId = 0;
            map.mouseHoverHive = null;
            map.mouseHoverEnemyId = 0;
            map.SetViewStar(null);
            ui.dfMonitor.trackingHives.RemoveAll(hive => hive == null || hive.starData == star);
            ui.dfMonitor.trackingBases.RemoveAll(b => b == null || b.id == 0 || b.groundSystem.planet.star == star);
            Sterilizer.Apply(data, star);
            Logger.LogInfo($"Sterilized star {star.id} ({star.displayName}): {capacity} cores.");
            UIMessageBox.Show("绝育完成", $"{star.displayName}：已清理黑雾，留下 {capacity} 个绝育中枢核心。", "确定", UIMessageBox.INFO);
        }
        catch (Exception e)
        {
            Logger.LogError(e);
            UIMessageBox.Show("绝育未完成", "执行中发生错误，部分改动可能已生效。\n请查看 BepInEx 日志；需要恢复时读取之前的存档。", "确定", UIMessageBox.ERROR);
        }
        finally
        {
            map.SetViewStar(viewedStar);
            ui.dfMonitor.OrganizeTargetList(true);
            ui.dfMonitor.RefreshEntries();
        }
    }
}
