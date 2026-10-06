using UnityEngine;
using UnityEditor;

[CustomPropertyDrawer(typeof(RangeSliderAttribute))]
public class RangeSliderDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var range = attribute as RangeSliderAttribute;

        if (property.propertyType == SerializedPropertyType.Generic)
        {
            var minProperty = property.FindPropertyRelative("min");
            var maxProperty = property.FindPropertyRelative("max");

            float minVal = minProperty.floatValue;
            float maxVal = maxProperty.floatValue;

            // Adding a prefix label to display the property name
            position = EditorGUI.PrefixLabel(position, GUIUtility.GetControlID(FocusType.Passive), label);

            // Create a Rect for the slider
            var sliderPosition = new Rect(position.x, position.y, position.width - 90, position.height);
            // Create Rects for the min and max values
            var minValPosition = new Rect(position.x + position.width - 90, position.y, 40, position.height);
            var maxValPosition = new Rect(position.x + position.width - 45, position.y, 40, position.height);

            EditorGUI.BeginChangeCheck();
            EditorGUI.MinMaxSlider(sliderPosition, ref minVal, ref maxVal, range.min, range.max);

            minVal = EditorGUI.FloatField(minValPosition, minVal);
            maxVal = EditorGUI.FloatField(maxValPosition, maxVal);

            if (EditorGUI.EndChangeCheck())
            {
                minProperty.floatValue = Mathf.Clamp(minVal, range.min, range.max);
                maxProperty.floatValue = Mathf.Clamp(maxVal, range.min, range.max);
            }
        }
        else
        {
            EditorGUI.LabelField(position, label.text, "Use RangeSlider with RangeFloat.");
        }
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return base.GetPropertyHeight(property, label) + 16;
    }
}
