using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;

static class Checks
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Main(string[] args)
    {
        Check(args.Length == 2, "Usage: NativeChecks <game-managed-dir> <bepinex-core-dir>");
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            foreach (string folder in args)
            {
                string file = Path.Combine(folder, new AssemblyName(e.Name).Name + ".dll");
                if (File.Exists(file)) return Assembly.LoadFrom(file);
            }
            return null;
        };
        Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void Run()
    {
        var assembly = Assembly.Load("BlueprintCamera");
        var canEnter = (Func<ECommand, bool, bool>)Delegate.CreateDelegate(typeof(Func<ECommand, bool, bool>),
            assembly.GetType("BlueprintCamera.Plugin", true).GetMethod("CanEnterFrom", BindingFlags.Static | BindingFlags.NonPublic));
        foreach (var command in new[] { ECommand.None, ECommand.Move, ECommand.Follow, ECommand.Mine })
        {
            Check(canEnter(command, false), "Normal hovering must allow photography: " + command);
            Check(!canEnter(command, true), "Pickup mode must block photography: " + command);
        }
        foreach (var command in new[] { ECommand.Hand, ECommand.Build, ECommand.Creative, ECommand.Plant })
            foreach (bool picking in new[] { false, true })
                Check(!canEnter(command, picking), "Active interaction must block photography: " + command);
        var patches = assembly.GetType("BlueprintCamera.CameraPatches", true);
        int count = 0;
        foreach (var method in patches.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
        {
            var attribute = method.GetCustomAttribute<HarmonyPatch>();
            if (attribute == null) continue;
            var target = AccessTools.DeclaredMethod(attribute.info.declaringType, attribute.info.methodName);
            Check(target != null && target.ReturnType == typeof(void), "Native patch target changed: " + attribute.info.declaringType + "." + attribute.info.methodName);
            foreach (var parameter in method.GetParameters())
                if (parameter.Name == "__instance") Check(parameter.ParameterType == target.DeclaringType, "Harmony instance ABI changed");
            count++;
        }
        Check(count == 9, "Expected nine native input, camera and display patch points");
        Check(typeof(GameCamera).GetProperty("main").PropertyType == typeof(UnityEngine.Camera), "main camera contract");
        Check(typeof(UIGame).GetField("canvasGroup").FieldType == typeof(UnityEngine.CanvasGroup), "HUD display contract");
        Check(typeof(PlayerController).GetProperty("modelVisible").CanWrite, "mecha display restoration contract");
        foreach (string field in new[] { "inputing", "onGUI", "onGUIOperate", "readyToBuild", "readyToPlant", "readyToEnterBlueprintMode" })
            Check(typeof(VFInput).GetField(field)?.FieldType == typeof(bool), "native input boundary: " + field);
        var v = typeof(UnityEngine.Vector3);
        var f = typeof(float);
        var hit = typeof(RCHCPU).MakeByRefType();
        Check(AccessTools.Method(typeof(Phys), "RayCastSphere", new[] { v, v, f, v, f, hit })?.ReturnType == typeof(bool), "sphere picking ABI");
        Check(AccessTools.Method(typeof(Phys), "RayCastOBB", new[] { v, v, f, v, v, typeof(UnityEngine.Quaternion), hit })?.ReturnType == typeof(bool), "box picking ABI");
        Check(AccessTools.Method(typeof(Phys), "RayCastCapsule", new[] { v, v, f, v, v, f, hit })?.ReturnType == typeof(bool), "capsule picking ABI");
        Console.WriteLine($"PASS: 16 photography entry cases, {count} native patch targets and camera/HUD/input ABI; Assembly-CSharp MVID {typeof(GameCamera).Module.ModuleVersionId}");
    }
}
