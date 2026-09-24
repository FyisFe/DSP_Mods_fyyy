using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using HarmonyLib;
using SideButtonBindings;
using UnityEngine;

internal static class Checks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Main(string[] args)
    {
        Require(args.Length == 1, "Usage: Checks <game-managed-directory>");
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string path = Path.Combine(args[0], new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        Require(AccessTools.Field(typeof(UIKeyEntry), "builtinKey")?.FieldType == typeof(BuiltinKey),
            "Harmony field binding changed");
        Require(AccessTools.Method(typeof(UIKeyEntry), "OverrideKey", new[] {
            typeof(GameOption), typeof(int), typeof(byte), typeof(bool) })?.ReturnType == typeof(bool),
            "Harmony target signature changed");
        Type patch = typeof(Plugin).Assembly.GetType("SideButtonBindings.SideButtonPatch", true);
        MethodInfo prefix = AccessTools.Method(patch, "Prefix");
        MethodInfo finalizer = AccessTools.Method(patch, "Finalizer");
        int cases = 0;
        foreach (int devices in new[] { 0, BuiltinKey.USE_KEYBOARD, BuiltinKey.USE_MOUSE,
                     BuiltinKey.USE_KEYBOARD | BuiltinKey.USE_MOUSE })
        foreach (int code in new[] { 0, (int)KeyCode.W, 323, 324, 325, 326, 327, 328, 329, 330, 1001, 1002 })
        for (byte modifier = 0; modifier < 8; modifier++)
        {
            var original = new BuiltinKey("Target", 1, (int)KeyCode.K, 0,
                ECombineKeyAction.OnceClick, false, true, 5 | devices);
            object[] state = { original, code, 0 };
            prefix.Invoke(null, state);
            var current = (BuiltinKey)state[0];
            Require(current.conflictKeyGroup == original.conflictKeyGroup, "real conflict groups changed");
            if (code >= 326 && code <= 329)
            {
                bool allowed = CombineKey.IsMouseKey(code, modifier, false)
                    ? (current.conflictGroup & BuiltinKey.USE_MOUSE) != 0
                    : (current.conflictGroup & BuiltinKey.USE_KEYBOARD) != 0;
                Require(allowed, "side button remains blocked by native device classification");

                var binding = new CombineKey(code, modifier, ECombineKeyAction.OnceClick, false);
                var xml = new StringBuilder();
                using (var writer = XmlWriter.Create(xml))
                {
                    writer.WriteStartElement("OverrideKey");
                    binding.ExportXML(writer);
                    writer.WriteEndElement();
                }
                var document = new XmlDocument();
                document.LoadXml(xml.ToString());
                var loaded = new CombineKey();
                loaded.ImportXML(document.DocumentElement);
                Require(loaded.IsEquals(code, modifier, false), "native XML round-trip changed binding");
                var other = new CombineKey(code, 8, ECombineKeyAction.OnceClick, false);
                Require(other.CanTriggerTogether(code, modifier, false, true), "native overlap semantics changed");
                Require(!other.CanTriggerTogether(code + 1, modifier, false, true), "different buttons overlap");
            }
            else
                Require(current.conflictGroup == original.conflictGroup, "non-side button restrictions changed");

            object[] cleanup = { current, state[2] };
            finalizer.Invoke(null, cleanup);
            Require(((BuiltinKey)cleanup[0]).conflictGroup == original.conflictGroup,
                "finalizer did not restore original device flags");
            cases++;
        }
        Console.WriteLine($"PASS: {cases} native device/conflict cases, XML round-trip and flag restoration; game MVID {typeof(UIKeyEntry).Module.ModuleVersionId}");
    }
}
