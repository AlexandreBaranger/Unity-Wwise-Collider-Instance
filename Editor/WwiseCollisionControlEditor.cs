#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WwiseCollisionControl))]
public class WwiseCollisionControlEditor : Editor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        WwiseCollisionControl wwiseCollisionControl = (WwiseCollisionControl)target;

        GUILayout.Space(10);

        EditorGUILayout.LabelField("GameObjects To Sync", EditorStyles.boldLabel);

        foreach (GameObject gameObject in wwiseCollisionControl.collisionEvents[0].gameObjectsToSync)
        {
            EditorGUILayout.ObjectField(gameObject, typeof(GameObject), true);
        }
    }
}
#endif
