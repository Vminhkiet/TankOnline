using UnityEngine;
using UnityEditor;

namespace Complete.Skills.Editor
{
    [CustomEditor(typeof(SkillData))]
    public class SkillDataEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Lặp qua tất cả property để vẽ Inspector mặc định, nhưng bỏ qua biến "parameters"
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                // Không vẽ "m_Script" nếu muốn (tuỳ chọn)
                if (iterator.name == "m_Script")
                {
                    GUI.enabled = false;
                    EditorGUILayout.PropertyField(iterator, true);
                    GUI.enabled = true;
                    continue;
                }

                if (iterator.name == "parameters")
                {
                    continue; // Bỏ qua parameters vì ta sẽ tự vẽ ở dưới
                }

                EditorGUILayout.PropertyField(iterator, true);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Gameplay Effects (Server Side)", EditorStyles.boldLabel);

            SerializedProperty parametersProp = serializedObject.FindProperty("parameters");
            SerializedProperty skillTypeProp = serializedObject.FindProperty("skillType");
            SkillType currentType = (SkillType)skillTypeProp.enumValueIndex;

            // Đảm bảo mảng luôn có 4 phần tử
            if (parametersProp.arraySize != 4)
            {
                parametersProp.arraySize = 4;
            }

            string[] labels = new string[4];
            for (int i = 0; i < 4; i++) labels[i] = $"Parameter {i}";

            // Map tên các tham số tùy theo SkillType
            switch (currentType)
            {
                case SkillType.Laser:
                    labels[0] = "Sát thương";
                    break;
                case SkillType.Dash:
                    labels[0] = "Khoảng cách lướt";
                    break;
                case SkillType.Buff:
                    labels[0] = "% Tăng sát thương";
                    labels[1] = "% Hồi máu mỗi giây";
                    break;
                case SkillType.ShieldDome:
                    labels[0] = "Máu của khiên";
                    labels[1] = "% Làm chậm kẻ địch";
                    break;
            }

            EditorGUI.indentLevel++;
            for (int i = 0; i < parametersProp.arraySize; i++)
            {
                SerializedProperty element = parametersProp.GetArrayElementAtIndex(i);
                
                // Kiểm tra xem parameter này có được gán tên đặc biệt không
                bool isUsed = (labels[i] != $"Parameter {i}");
                
                if (isUsed || currentType == SkillType.Generic)
                {
                    EditorGUILayout.PropertyField(element, new GUIContent(labels[i]));
                }
                else
                {
                    // Nếu là Generic hoặc không dùng thì có thể vẽ mờ đi để người dùng biết là không cần điền
                    GUI.enabled = false;
                    EditorGUILayout.PropertyField(element, new GUIContent($"(Không dùng) Parameter {i}"));
                    GUI.enabled = true;
                }
            }
            EditorGUI.indentLevel--;

            serializedObject.ApplyModifiedProperties();
        }
    }
}
