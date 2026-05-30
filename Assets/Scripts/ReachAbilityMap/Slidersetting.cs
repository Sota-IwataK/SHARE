using System;
using System.Reflection;
using UnityEngine;
using MixedReality.Toolkit.UX;

[AddComponentMenu("UX/Property Slider Binder")]
public class Slidersetting : MonoBehaviour
{
    [Header("Slider")]
    [Tooltip("スケール用の MRTK Slider コンポーネントをドラッグ＆ドロップ")]
    public Slider slider;

    [Header("ターゲット設定")]
    [Tooltip("値を変更したい GameObject を指定")]
    public GameObject targetObject;

    [Tooltip("変更したい Component をドラッグ＆ドロップ")]
    public PointCloudWithSpheres targetComponent;

    [Tooltip("Field または Property の名前 (例: \"sphereScale\" や \"_BaseColor.a\" など)")]
    public string memberName;

    // 内部キャッシュ
    private FieldInfo _field;
    private PropertyInfo _prop;

    void Awake()
    {
        if (targetComponent == null || string.IsNullOrEmpty(memberName))
        {
            Debug.LogError("PropertySliderBinder: targetComponent と memberName を必ず設定してください。");
            enabled = false;
            return;
        }

        var type = targetComponent.GetType();
        // まずプロパティを探す
        _prop = type.GetProperty(memberName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (_prop == null)
        {
            // 見つからなければフィールドを探す
            _field = type.GetField(memberName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }

        if (_prop == null && _field == null)
        {
            Debug.LogError($"PropertySliderBinder: `{memberName}` が見つかりませんでした on {type.Name}.");
            enabled = false;
            return;
        }
    }

    void OnEnable()
    {
        if (slider != null)
        {
            // イベント登録
            slider.OnValueUpdated.AddListener(OnSliderValueChanged);
            // 最初の反映
            OnSliderValueChanged(new SliderEventData(slider.Value, slider.Value));
        }
    }

    void OnDisable()
    {
        if (slider != null)
        {
            slider.OnValueUpdated.RemoveListener(OnSliderValueChanged);
        }
    }

    private void OnSliderValueChanged(SliderEventData data)
    {
        float v = data.NewValue;
        try
        {
            if (_prop != null && _prop.CanWrite && _prop.PropertyType == typeof(float))
            {
                _prop.SetValue(targetComponent, v);
            }
            else if (_field != null && _field.FieldType == typeof(float))
            {
                _field.SetValue(targetComponent, v);
            }
            else
            {
                Debug.LogWarning($"PropertySliderBinder: `{memberName}` は float ではないか、書き込み不可です。");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"PropertySliderBinder: 値の設定で例外: {ex}");
        }
    }
}
