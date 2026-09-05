using System;
using System.IO;
using BepInEx.Configuration;
using BepInEx;
using UnityEngine;

namespace IcarusModelReplacement;

[BepInPlugin("org.fyyy.icarusmodelreplacement", "Icarus Model Replacement", "1.0.0")]
public sealed class Plugin : BaseUnityPlugin
{
    private const string Builtin = "builtin";
    private MechaArmorModel armor;
    private Model model;
    private ConfigEntry<string> modelDirectory;
    private string selected;
    private HiddenRenderer[] parts = Array.Empty<HiddenRenderer>();
    private HiddenRenderer[] bones = Array.Empty<HiddenRenderer>();
    private HiddenRenderer[] wrecks = Array.Empty<HiddenRenderer>();
    private GameObject wreckageGroup;

    private void Awake()
    {
        modelDirectory = Config.Bind("Model", "Directory", Builtin,
            "builtin uses the included Gugugaga. Otherwise, a model folder relative to BepInEx/plugins or absolute. Empty keeps vanilla Icarus. Changes apply in game.");
    }

    private void LateUpdate()
    {
        var player = GameMain.isRunning ? GameMain.mainPlayer : null;
        var next = player?.mechaArmorModel;
        if (next != null && !next.inited)
            next = null;

        string selection = modelDirectory.Value.Trim();
        // Managed identity lets destroyed armor trigger model resource cleanup.
        if (!ReferenceEquals(armor, next) || selected != selection)
        {
            Clear();
            armor = next;
            selected = selection;
            if (armor != null && selection.Length != 0)
            {
                try
                {
                    // Capture vanilla renderers before adding our skin under the same Model node.
                    var renderers = armor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                    var directory = selection == Builtin
                        ? Path.Combine(Path.GetDirectoryName(Info.Location), "model")
                        : Path.Combine(Paths.PluginPath, selection);
                    var pack = ModelPack.Load(directory);
                    // Keep Icarus visible until the model and material are ready.
                    model = new Model(pack, player.controller.model, armor.gameObject.layer);
                    parts = Hide(renderers);
                    bones = new HiddenRenderer[armor.boneModels.Length];
                    Logger.LogInfo($"Model attached: {pack.Info.Name} by {pack.Info.Author} ({pack.Info.License}).");
                }
                catch (Exception ex)
                {
                    Clear();
                    Logger.LogError($"Cannot load model '{selection}'; keeping Icarus. {ex}");
                    return;
                }
            }
        }

        if (armor == null || model == null)
            return;

        // Custom armor can replace its renderer without replacing the player's model.
        for (int i = 0; i < bones.Length; i++)
            bones[i].Set(armor.boneModels[i]?.meshRenderer);

        if (wreckageGroup != armor.wreckageGroup)
        {
            Restore(wrecks);
            wreckageGroup = armor.wreckageGroup;
            wrecks = wreckageGroup != null
                ? Hide(wreckageGroup.GetComponentsInChildren<MeshRenderer>(true))
                : Array.Empty<HiddenRenderer>();
        }

        model.Root.SetActive(armor.active && player.isAlive);
        model.UpdateLighting();
        var animator = player.animator;
        if (animator.run_slow == null)
            return;

        float air = Mathf.Clamp01(animator.driftWeight + animator.flyWeight + animator.sailWeight);
        var state = Motion.Sample(animator.run_slow.normalizedTime * Math.PI * 2,
            Mathf.Clamp01(animator.runWeight), air, Mathf.Clamp01(animator.sailWeight));
        model.Animate(state);
    }

    private static HiddenRenderer[] Hide(Renderer[] renderers)
    {
        var hidden = new HiddenRenderer[renderers.Length];
        for (int i = 0; i < hidden.Length; i++)
            hidden[i].Set(renderers[i]);
        return hidden;
    }

    private static void Restore(HiddenRenderer[] renderers)
    {
        for (int i = 0; i < renderers.Length; i++)
            renderers[i].Set(null);
    }

    private void OnDisable()
    {
        Clear();
        armor = null;
    }

    private void Clear()
    {
        Restore(parts);
        Restore(bones);
        Restore(wrecks);
        parts = bones = wrecks = Array.Empty<HiddenRenderer>();
        model?.Dispose();
        model = null;
        wreckageGroup = null;
    }

    private struct HiddenRenderer
    {
        private Renderer renderer;
        private bool wasHidden;

        public void Set(Renderer next)
        {
            if (renderer == next) return;
            if (renderer != null)
                renderer.forceRenderingOff = wasHidden;
            renderer = next;
            if (renderer != null)
            {
                wasHidden = renderer.forceRenderingOff;
                // enabled also controls vanilla colliders and death wreckage.
                renderer.forceRenderingOff = true;
            }
        }
    }
}
