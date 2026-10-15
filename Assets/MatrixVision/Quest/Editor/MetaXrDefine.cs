using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace MatrixVision.Quest.EditorTools
{
    /// <summary>
    /// Ajoute le symbole MATRIX_META_XR quand MR Utility Kit (Meta XR SDK) est installé, et le retire sinon.
    /// Le projet compile ainsi avec ou sans le SDK : MetaQuestRoom n'existe qu'avec lui.
    /// </summary>
    [InitializeOnLoad]
    static class MetaXrDefine
    {
        const string Symbol = "MATRIX_META_XR";
        const string MrukType = "Meta.XR.MRUtilityKit.MRUK";

        static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.Android, NamedBuildTarget.Standalone };

        static MetaXrDefine()
        {
            bool installed = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetType(MrukType, false) != null);
            foreach (var target in Targets)
            {
                var symbols = PlayerSettings.GetScriptingDefineSymbols(target)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
                bool present = symbols.Contains(Symbol);
                if (installed == present)
                    continue;

                if (installed)
                    symbols.Add(Symbol);
                else
                    symbols.Remove(Symbol);
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
            }
        }
    }
}
