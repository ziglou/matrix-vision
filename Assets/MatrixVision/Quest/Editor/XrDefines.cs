using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace MatrixVision.Quest.EditorTools
{
    /// <summary>
    /// Ajoute ou retire les symboles de compilation selon les paquets XR installés :
    /// MATRIX_AR_FOUNDATION (AR Foundation, avec Unity OpenXR: Meta) et MATRIX_XRI (XR Interaction Toolkit).
    /// Le projet compile ainsi avec ou sans eux : ArQuestRoom et la saisie des pilules n'existent qu'avec eux.
    /// </summary>
    [InitializeOnLoad]
    static class XrDefines
    {
        static readonly (string symbol, string type)[] Packages =
        {
            ("MATRIX_AR_FOUNDATION", "UnityEngine.XR.ARFoundation.ARPlaneManager"),
            ("MATRIX_XRI", "UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable"),
        };

        static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.Android, NamedBuildTarget.Standalone };

        static XrDefines()
        {
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var target in Targets)
            {
                var symbols = PlayerSettings.GetScriptingDefineSymbols(target)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
                bool changed = false;
                foreach (var (symbol, type) in Packages)
                {
                    bool installed = assemblies.Any(a => a.GetType(type, false) != null);
                    if (installed == symbols.Contains(symbol))
                        continue;

                    if (installed)
                        symbols.Add(symbol);
                    else
                        symbols.Remove(symbol);
                    changed = true;
                }

                // Ancien symbole de la version Meta XR SDK, abandonnée pour OpenXR.
                changed |= symbols.Remove("MATRIX_META_XR");
                if (changed)
                    PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", symbols));
            }
        }
    }
}
