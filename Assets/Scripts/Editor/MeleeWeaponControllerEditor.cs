using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(MeleeWeaponController))]
public class MeleeWeaponControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        MeleeWeaponController controller = (MeleeWeaponController)target;

        if (GUILayout.Button("Set Attack Rotation From Current Transform"))
        {
            Undo.RecordObject(controller, "Set Attack Rotation From Current Transform");
            controller.SetAttackRotationFromCurrentTransform();
            EditorUtility.SetDirty(controller);
        }
    }
} 