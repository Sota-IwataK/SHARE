using RosMessageTypes.Sensor;
using RosMessageTypes.Std;
using UnityEngine;

public class ImagePublisher3 : RosTcpPublisher<ImageMsg>
{
    public Texture2D texture;
    public string FrameId = "";

    private ImageMsg imageMessage;

    protected override void Start()
    {
        base.Start();
        InitializeImageMessage();
    }

    private void InitializeImageMessage()
    {
        if (texture == null) return;

        imageMessage = new ImageMsg
        {
            header = new HeaderMsg { frame_id = FrameId },
            height = (uint)texture.height,
            width = (uint)texture.width,
            encoding = "png",
            is_bigendian = 0,
            step = (uint)(texture.width * 3),
            data = new byte[0]
        };
    }

    private void Update()
    {
        if (texture == null) return;

        if (imageMessage == null)
            InitializeImageMessage();

        imageMessage.header = new HeaderMsg
        {
            stamp = RosTcpUtility.GetRosTime(),
            frame_id = FrameId
        };
        imageMessage.data = texture.EncodeToPNG();
        Publish(imageMessage);
    }
}
