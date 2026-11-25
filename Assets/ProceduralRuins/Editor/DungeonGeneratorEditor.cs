using UnityEditor;
using UnityEngine;

namespace ProceduralRuins.EditorTools
{
    [CustomEditor(typeof(DungeonGenerator))]
    public class DungeonGeneratorEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var generator = (DungeonGenerator)target;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Dungeon Generator", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Gerar Calabouço", GUILayout.Height(30)))
                {
                    Undo.RegisterFullObjectHierarchyUndo(generator.gameObject, "Gerar Calabouço");
                    generator.Generate();
                    EditorUtility.SetDirty(generator);
                }

                if (GUILayout.Button("Limpar", GUILayout.Height(30)))
                {
                    Undo.RegisterFullObjectHierarchyUndo(generator.gameObject, "Limpar Calabouço");
                    generator.Clear();
                    EditorUtility.SetDirty(generator);
                }
            }

            if (GUILayout.Button("Gerar com Nova Seed Aleatória"))
            {
                Undo.RecordObject(generator, "Nova Seed");
                generator.seed = -1;
                generator.Generate();
                EditorUtility.SetDirty(generator);
            }
        }
    }
}
