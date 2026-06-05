/*using UnityEngine;

public class PointCloudColorChanger : MonoBehaviour
{
    public MockPointCloudSubscriber mockSubscriber;
    public float distanceThreshold = 0.5f;
    public Color targetColor = Color.red;
    public float updateInterval = 1.0f;

    private GameObject bottle;
    private Vector3 lastBottlePosition;
    private float checkDistanceThreshold = 0.1f;

    void Start()
    {
        if (mockSubscriber == null)
        {
            mockSubscriber = FindObjectOfType<MockPointCloudSubscriber>();
        }

        bottle = GameObject.FindGameObjectWithTag("bottle");
        if (bottle != null)
        {
            lastBottlePosition = bottle.transform.position;
        }

        InvokeRepeating(nameof(UpdatePointColors), 0f, updateInterval);
    }

    void UpdatePointColors()
    {
        if (bottle != null)
        {
            Vector3 bottlePosition = bottle.transform.position;

            if (Vector3.Distance(lastBottlePosition, bottlePosition) > checkDistanceThreshold)
            {
                lastBottlePosition = bottlePosition;
                Vector3[] points = mockSubscriber.GetPCL();
                Color[] currentColors = mockSubscriber.GetPCLColor();

                for (int i = 0; i < points.Length; i++)
                {
                    float distance = Vector3.Distance(points[i], bottlePosition);
                    Color currentColor = currentColors[i];

                    // ‹——£“à ¨ Ô‚É‚·‚é
                    if (distance <= distanceThreshold && currentColor != targetColor)
                    {
                        mockSubscriber.UpdatePointColor(i, targetColor);
                    }
                    // ‹——£ŠO‚ÅÔ‚¾‚Á‚½ ¨ ”’‚É–ß‚·
                    else if (distance > distanceThreshold && currentColor == targetColor)
                    {
                        mockSubscriber.UpdatePointColor(i, Color.white);
                    }
                }
            }
        }
    }
}
*/