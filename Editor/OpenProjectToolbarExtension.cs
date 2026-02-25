#if UNITY_6000_0_OR_NEWER
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;

namespace Hierarchy2
{
    public static class OpenProjectToolbarExtension
    {
        [MainToolbarElement("Hierarchy2/OpenProject", defaultDockPosition = MainToolbarDockPosition.Middle)]
        public static MainToolbarElement CreateOpenProjectButton()
        {
            var icon = EditorGUIUtility.IconContent("d_cs Script Icon").image as Texture2D;
            var content = new MainToolbarContent(icon, "Open C# Project in Default Editor");

            return new MainToolbarButton(content, () =>
            {
                EditorApplication.ExecuteMenuItem("Assets/Open C# Project");
            });
        }
    }
}
#endif
