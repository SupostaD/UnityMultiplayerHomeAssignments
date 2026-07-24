using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[CustomEditor(typeof(HexMapGenerator))]
public sealed class HexMapGeneratorEditor : Editor
{
    private void OnEnable()
    {
        if (target is HexMapGenerator generator)
            generator.RebuildMapIndex();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        HexMapGenerator generator = (HexMapGenerator)target;

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField(
            "Editor Map Preview",
            EditorStyles.boldLabel
        );
        EditorGUILayout.HelpBox(
            "Place existing HexTile objects manually under Base Tiles Root " +
            "or Authored Tiles Root. The generator registers those children; " +
            "it does not create islands or stairs.",
            MessageType.Info
        );

        EditorGUILayout.LabelField(
            "Saved base tiles",
            generator.BaseGeneratedTileCount.ToString()
        );
        EditorGUILayout.LabelField(
            "Manually placed authored tiles",
            generator.AuthoredTileCount.ToString()
        );
        EditorGUILayout.LabelField(
            "Total networked tiles",
            generator.GeneratedTileCount.ToString()
        );

        if (generator.DuplicateCoordinateCount > 0)
        {
            EditorGUILayout.HelpBox(
                $"{generator.DuplicateCoordinateCount} duplicate map " +
                "coordinate(s) were found. Move or remove overlapping " +
                "authored tiles before playing.",
                MessageType.Error
            );
        }

        if (generator.GeneratedTileCount >
            HexTerritoryManager.MaximumNetworkedTiles)
        {
            EditorGUILayout.HelpBox(
                $"The map exceeds the network limit of " +
                $"{HexTerritoryManager.MaximumNetworkedTiles} tiles.",
                MessageType.Error
            );
        }

        if (GUILayout.Button("Refresh Existing Tile References"))
        {
            Undo.RegisterFullObjectHierarchyUndo(
                generator.gameObject,
                "Refresh Existing Hex Tiles"
            );
            generator.RebuildMapIndex();
            MarkGeneratorDirty(generator);
        }

        EditorGUILayout.Space(8f);

        if (generator.BaseGeneratedTileCount == 0)
        {
            if (GUILayout.Button("Generate Base Map In Editor"))
                GenerateBaseMap(generator);
        }
        else
        {
            EditorGUILayout.HelpBox(
                "The saved base map will be reused at runtime. It will not " +
                "be generated again while Base Tiles Root contains tiles.",
                MessageType.None
            );

            if (GUILayout.Button("Regenerate Base Map In Editor..."))
            {
                bool confirmed = EditorUtility.DisplayDialog(
                    "Regenerate base map?",
                    "This deletes and recreates only the tiles under Base " +
                    "Tiles Root. Authored Tiles are preserved.",
                    "Regenerate",
                    "Cancel"
                );

                if (confirmed)
                    GenerateBaseMap(generator);
            }
        }
    }

    private static void GenerateBaseMap(
        HexMapGenerator generator)
    {
        Undo.RegisterFullObjectHierarchyUndo(
            generator.gameObject,
            "Generate Hex Base Map"
        );
        generator.Generate();
        MarkGeneratorDirty(generator);
    }

    private static void MarkGeneratorDirty(
        HexMapGenerator generator)
    {
        generator.RebuildMapIndex();
        EditorUtility.SetDirty(generator);
        PrefabUtility.RecordPrefabInstancePropertyModifications(
            generator
        );

        if (generator.gameObject.scene.IsValid())
        {
            EditorSceneManager.MarkSceneDirty(
                generator.gameObject.scene
            );
        }

        SceneView.RepaintAll();
    }
}
