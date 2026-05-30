using UnityEngine;
using UnityEngine.UI;
using TMPro;
using RosSharp.RosBridgeClient;

public class GameUI : MonoBehaviour
{
    [SerializeField] private RectTransform FxHolder;
    [SerializeField] private Image Circle_image;
    [SerializeField] private GameObject rosConnector;
    [SerializeField] private TextMeshProUGUI textProgress;

    [SerializeField] private int startCount = 3;
    [SerializeField] private float lapDuration = 1f;

    private int currentCount;
    private float timer;

  

    // アクティブになるたびにタイマー初期化
    private void OnEnable()
    {
        currentCount = startCount;
        timer = 0f;
        UpdateText();
        Circle_image.fillAmount = 0f;
        FxHolder.rotation = Quaternion.Euler(0f, 0f, 0f);
    }

    private void Update()
    {
        if (currentCount <= 0)
        {
            /*rosConnector.GetComponent<handPosePublisher>().enabled = true;
            rosConnector.GetComponent<airTapPublisher>().enabled = true;
            rosConnector.GetComponent<Float32MultiSubscriber>().enabled = true;*/
            gameObject.SetActive(false);
            return;
        }

        timer += Time.deltaTime;
        float progress = Mathf.Clamp01(timer / lapDuration);

        Circle_image.fillAmount = progress;
        FxHolder.rotation = Quaternion.Euler(0f, 0f, -progress * 360f);

        if (timer >= lapDuration)
        {
            timer -= lapDuration;
            currentCount--;
            UpdateText();
        }
    }

    private void UpdateText()
    {
        textProgress.text = currentCount > 0 ? currentCount.ToString() : "";
    }
}
