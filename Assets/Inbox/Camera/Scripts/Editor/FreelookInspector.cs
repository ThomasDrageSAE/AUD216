using UnityEditor;

[CustomEditor(typeof(FreelookCamera))]
public class FreelookCameraInspector : Editor {
	private static bool boostOptionsFoldout = false;

	public override void OnInspectorGUI() {
		//base.OnInspectorGUI();
		FreelookCamera cam = target as FreelookCamera;
		if(cam == null)
			return;

		serializedObject.Update();

		EditorGUILayout.PropertyField(serializedObject.FindProperty("Speed"));

		boostOptionsFoldout = EditorGUILayout.Foldout(boostOptionsFoldout, "Boost Options");
		if (boostOptionsFoldout) {
			EditorGUI.indentLevel++;
			EditorGUILayout.PropertyField(serializedObject.FindProperty("EnableBoostSpeed"));
			EditorGUI.BeginDisabledGroup(!cam.EnableBoostSpeed);
			EditorGUILayout.PropertyField(serializedObject.FindProperty("BoostKey"));
			EditorGUILayout.PropertyField(serializedObject.FindProperty("BoostSpeed"));
			EditorGUI.EndDisabledGroup();
			EditorGUI.indentLevel--;
		}

		EditorGUILayout.PropertyField(serializedObject.FindProperty("MouseSensitivity"));
		EditorGUILayout.PropertyField(serializedObject.FindProperty("LockCursor"));
		EditorGUILayout.PropertyField(serializedObject.FindProperty("ToggleKey"));
		EditorGUILayout.PropertyField(serializedObject.FindProperty("UpKey"));
		EditorGUILayout.PropertyField(serializedObject.FindProperty("DownKey"));

		serializedObject.ApplyModifiedProperties();
	}
}
